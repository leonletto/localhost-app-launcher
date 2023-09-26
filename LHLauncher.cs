using Microsoft.Win32;
using System.Diagnostics;
using System.Threading;
using System.Security.Principal;
using System.Reflection;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows.Forms;
using System.Net.Security;
using System.IO;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Timer = System.Threading.Timer;


namespace LHLauncher
{
    public class ConfiguredProgram
    {
        public bool IsValid { get; set;}
        public string? ProgramName { get; set; }
        public string? ProcessToVerify { get; set; }
        public string? CommandToRun { get; set; }
        
    }

    public partial class LHLauncher : Form
    {
        private static string GetHostNameForCertificate()
        {
            var hostName = (string?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\LHLauncher", "hostName", null);

            if (!string.IsNullOrEmpty(hostName))
                return hostName;

            // Default to localhost.com
            hostName = "localhost.com";

            return hostName;
        }
        
        private static string GetAppCatalogURL()
        {
            var appCatalogURL = (string?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\LHLauncher", "appCatalogURL", null);

            if (!string.IsNullOrEmpty(appCatalogURL))
                return appCatalogURL;

            // Default to localhost.com
            var url = GetHostNameForCertificate();
            appCatalogURL = "https://{url}".Replace("{url}", url + "/config");

            return appCatalogURL;
        }
        
        private string GetLoggingPath()
        {
            var loggingPath = (string?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\LHLauncher", "loggingPath", null);

            if (!string.IsNullOrEmpty(loggingPath))
                return loggingPath;

            // Get the ProgramData path and create a directory for LHLauncher if it doesn't exist
            string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LHLauncher");
            if (!Directory.Exists(defaultPath))
            {
                Directory.CreateDirectory(defaultPath);
            }

            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            loggingPath = Path.Combine(defaultPath, Path.GetFileNameWithoutExtension(assemblyLocation) + ".log");

            return loggingPath;
        }

        private void Log(string message)
        {
            // Get the current stack trace and the previous frame (caller)
            var frame = new StackTrace(1, true).GetFrame(0); // '1' skips one frame to get the caller of this method
            if (frame == null) return;
            var fileName = Path.GetFileNameWithoutExtension(frame.GetFileName());
            var lineNo = frame.GetFileLineNumber();

            var loggingPath = GetLoggingPath();
            var logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{fileName}:{lineNo}] {message}";
            File.AppendAllText(loggingPath, logMessage + Environment.NewLine);
        }


        private readonly NotifyIcon _trayIcon;
        
        private X509Certificate2? serverCertificate;
        private List<ConfiguredProgram> programs = new List<ConfiguredProgram>();
        
        // Add timer for reload
        private Timer registryCheckTimer;
        private readonly object programsLock = new object();
        
        // P/Invoke declarations
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, ref INPUT pInputs, int cbSize);

        private const int SW_RESTORE = 9;

        private struct INPUT
        {
            public int type;
            public MOUSEINPUT mi;
        }

        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public int mouseData;
            public int dwFlags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        private void SimulateUserInput()
        {
            INPUT input = new INPUT();
            input.type = 0; // MOUSE input
            input.mi.dx = 0;
            input.mi.dy = 0;
            input.mi.dwFlags = 0x1; // MOUSEEVENTF_MOVE

            SendInput(1, ref input, Marshal.SizeOf(typeof(INPUT)));
        }

        private void BringProcessToFront(Process process)
        {
            if (process.MainWindowHandle == IntPtr.Zero) return; // Process doesn't have a main window

            ShowWindow(process.MainWindowHandle, SW_RESTORE);
            SimulateUserInput();
            SetForegroundWindow(process.MainWindowHandle);
        }
        
        private void RegistryCheckCallback(object state)
        {
            // Reload the programs from the registry.
            LoadConfiguredPrograms();
        }

        public LHLauncher()
        {
            InitializeComponent();
            
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            
            // Initialize the timer to check the registry every 5 seconds.
            registryCheckTimer = new Timer(RegistryCheckCallback, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));

            // Create tray menu and items
            var trayMenu = new ContextMenuStrip();
            var exitMenuItem = new ToolStripMenuItem("Exit");
            exitMenuItem.Click += OnExit;
            trayMenu.Items.Add(exitMenuItem);
            var viewLogMenuItem = new ToolStripMenuItem("View Log");
            viewLogMenuItem.Click += ViewLog_Click;
            trayMenu.Items.Add(viewLogMenuItem);
            var viewConfigMenuItem = new ToolStripMenuItem("View Config");
            viewConfigMenuItem.Click += ViewConfig_Click;
            trayMenu.Items.Add(viewConfigMenuItem);


            // Create tray icon
            _trayIcon = new NotifyIcon();
            _trayIcon.Text = "LHLauncher";
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LHLauncher.ico");
            _trayIcon.Icon = new Icon(iconPath);
            _trayIcon.ContextMenuStrip = trayMenu;
            _trayIcon.Visible = true;

            // Start the HTTPS listener
            LoadConfiguredPrograms();
            StartSslListener();
        }
        
        private void LoadConfiguredPrograms()
        {

            var baseRegistryKey = Registry.CurrentUser.OpenSubKey(@"Software\LHLauncher");
            if (baseRegistryKey == null)
            {
                Log("Registry key - HKEY_CURRENT_USER\\Software\\LHLauncher - not found.");
                return;
            }
            
            var subKeyNames = baseRegistryKey.GetSubKeyNames();
            if (subKeyNames.Length == 0)
            {
                Log("No configured programs found.");
                return;
            }

            lock (programsLock)
            {
                foreach (var programName in subKeyNames)
                {
                    using var subKey = baseRegistryKey.OpenSubKey(programName);
                    if (subKey == null) continue;
                    var processToVerifyTemp = (string?)subKey.GetValue("ProcessName", null);
                    processToVerifyTemp = processToVerifyTemp?.Trim('"').ToLower();
                    var program = new ConfiguredProgram
                    {
                        ProgramName = programName,
                        CommandToRun = (string?)subKey.GetValue("Command", null),
                        ProcessToVerify = processToVerifyTemp,
                        IsValid = !string.IsNullOrEmpty((string?)subKey.GetValue("Command", null)) &&
                                  !string.IsNullOrEmpty((string?)subKey.GetValue("ProcessName", null))
                    };
                    programs.Add(program);
                }
            }
        }

        
        private void ViewLog_Click(object? sender, EventArgs e)
        {
            string logPath = GetLoggingPath();
            Process.Start("notepad.exe", logPath);
        }

        private string GenerateConfigHtml()
        {
            var baseRegistryKey = Registry.CurrentUser.OpenSubKey(@"Software\LHLauncher");
            if (baseRegistryKey == null)
            {
                return "Registry key - HKEY_CURRENT_USER\\Software\\LHLauncher - not found.";
            }

            var productVersion = (string?)baseRegistryKey.GetValue("ProductVersion", "Unknown");
            var htmlBuilder = new StringBuilder();
            htmlBuilder.Append("<html><head><title>LHLauncher Config</title>");
            htmlBuilder.Append("<style>");
            htmlBuilder.Append("body { font-family: Arial, sans-serif; }");
            htmlBuilder.Append("table { width: 100%; border-collapse: collapse; margin-top: 20px; }");
            htmlBuilder.Append("th, td { padding: 10px; text-align: left; }");
            htmlBuilder.Append("th { background-color: #f2f2f2; }");
            htmlBuilder.Append("tr:hover { background-color: #f5f5f5; }");
            htmlBuilder.Append("</style>");
            htmlBuilder.Append("<script>");
            htmlBuilder.Append("function openInNewTab(url) {");
            htmlBuilder.Append("  window.open(url, '_blank');}");
            htmlBuilder.Append("</script>");
            htmlBuilder.Append("</head><body>");
            htmlBuilder.Append($"<h1>Localhost App Launcher Version {productVersion}</h1>");
            htmlBuilder.Append($"<h2>App Catalog: <a href='{GetAppCatalogURL()}'>{GetAppCatalogURL()}</a></h2>");
            htmlBuilder.Append("<h2>Currently Configured LHLauncher Applications</h2>");
            htmlBuilder.Append("<table border='1'><thead><tr><th>Name</th><th>Process Name</th><th>Process to Launch</th><th>Test URL</th></tr></thead><tbody>");

            foreach (string programName in baseRegistryKey.GetSubKeyNames())
            {
                using (var subkey = baseRegistryKey.OpenSubKey(programName))
                {
                    if (subkey != null)
                    {
                        var commandToRun = (string?)subkey.GetValue("Command", null);
                        var processToVerify = (string?)subkey.GetValue("ProcessName", null);
                        if (string.IsNullOrEmpty(commandToRun) || string.IsNullOrEmpty(processToVerify)) continue;
                        var urlToOpen = "https://" + GetHostNameForCertificate() + "/" + programName;
                        htmlBuilder.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td><a href='javascript:void(0)' onclick='openInNewTab(\"{3}\")'>Open {0}</a></td></tr>", programName, processToVerify, commandToRun, urlToOpen);
                    }
                }
            }
            htmlBuilder.Append("</tbody></table></body></html>");
            return htmlBuilder.ToString();
        }
        
        private void ViewConfig_Click(object? sender, EventArgs e)
        {
            // string configHtml = GenerateConfigHtml();
            // string tempFilePath = Path.Combine(Path.GetTempPath(), "LHLauncherConfig.html");
            // File.WriteAllText(tempFilePath, configHtml);
            // Process.Start(new ProcessStartInfo("cmd", $"/c start {tempFilePath}") { CreateNoWindow = true });
            Process.Start(new ProcessStartInfo("cmd", $"/c start https://localhost.com/config") { CreateNoWindow = true });
        }


        
        // Override the OnLoad method to hide the main form
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Hide();
        }
        
        // Handle the FormClosing event to prevent the form from being shown
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // If the close reason is a user closing the form, 
            // minimize it and cancel the close operation
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                WindowState = FormWindowState.Minimized;
                Hide();
            }
            else
            {
                base.OnFormClosing(e);
            }
        }

        private static void SendDefaultResponse(StreamWriter writer)
        {
            const string responseString = "<html><head><title>LHLauncher</title></head><body><h1>LHLauncher is listening...</h1></body></html>";
            SendResponse(writer, responseString);
        }
        
        private static void SendResponse(TextWriter writer, string responseString = "OK", HttpStatusCode statusCode = HttpStatusCode.OK, bool closeBrowser = false)
        {
            if (closeBrowser)
            {
               var appCatalogUrl = GetAppCatalogURL();
                responseString = @"
<html>
<head>
    <script>

        const closeWindow = () => {
            
            var test = window.close();
            if (test === undefined) {
                window.location.href = '{appCatalogUrl}';
            }
        };
        closeWindow();
    </script>
    
</head>
<body>
    <button id='myButton' onclick='closeWindow()'>Close Window</button>
</body>
</html>".Replace("{appCatalogUrl}", appCatalogUrl);
            }

            var buffer = Encoding.UTF8.GetBytes(responseString);
    
            if (statusCode != HttpStatusCode.OK) writer.WriteLine($"HTTP/1.1 {(int)statusCode} {statusCode}");

            writer.WriteLine("HTTP/1.1 200 OK");
            writer.WriteLine($"Content-Length: {buffer.Length}");
            writer.WriteLine("Connection: close");
            writer.WriteLine("Content-Type: text/html; charset=UTF-8");
            writer.WriteLine();
            writer.Write(responseString);
        }


        
        private void SendImageResponse(StreamWriter writer, byte[] imageBytes)
        {
            writer.WriteLine("HTTP/1.1 200 OK");
            writer.WriteLine("Content-Type: image/png");
            writer.WriteLine($"Content-Length: {imageBytes.Length}");
            writer.WriteLine(); // Empty line to separate headers from body
            writer.BaseStream.Write(imageBytes, 0, imageBytes.Length);
        }

       
        private void OnExit(object? sender, EventArgs e)
        {
            try
            {
                // Console.WriteLine("Disposing tray icon...");
                _trayIcon.Dispose();
                // Console.WriteLine("Tray icon disposed.");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                // Console.WriteLine("Exiting application.");
                Application.Exit();
            }
        }
        
        private async Task HandleSecureClient(TcpClient client)
        {
            // Create an SSL stream for the new client
            var sslStream = new SslStream(client.GetStream(), false);

            // Authenticate as the server
            if (serverCertificate != null) sslStream.AuthenticateAsServer(serverCertificate); else
            {
                Log("Server certificate not found. Exiting...");
                return;
            }

            // Read the request from the client
            var reader = new StreamReader(sslStream);
            var requestBuilder = new StringBuilder();
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            {
                requestBuilder.AppendLine(line);
            }
            var request = requestBuilder.ToString();

            // Log the request for debugging
            // Log($"Received request: {request}");
            // Create a StreamWriter for sending responses
            var writer = new StreamWriter(sslStream);
            
            // Check for allowed HTTP methods
            if (!request.StartsWith("GET "))
            {
                Log("Invalid request method.");
                SendResponse(writer, "Invalid request method.", HttpStatusCode.MethodNotAllowed);
            }

            // Extract the request path from the request string
            var requestLine = request.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            var requestPath = requestLine?.Split(' ')[1];
            
           

            // Validate the extracted request path
            const string pathPattern = @"^(/[\w\-\.]*|/?)$";
            var match = Regex.Match(requestPath ?? string.Empty, pathPattern);

            if (!match.Success)
            {
                Log("Invalid request path.");
                SendResponse(writer, "Invalid request path.", HttpStatusCode.BadRequest);
                return;  // Ensure you exit out of the function after sending an error response.
            }  

            // Check request length
            if (request.Length > 2048) 
            {
                Log("Request too long.");
                SendResponse(writer, "Request too long.", HttpStatusCode.RequestEntityTooLarge);
            }

            var requestedProgram = programs.FirstOrDefault(p => requestPath != null && requestPath.Equals($"/{p.ProgramName}"));

            if (requestedProgram != null)
            {
                var didRun = ExecuteAndMonitorProgram(requestedProgram);
                if (didRun == "NotValid")
                {
                    SendResponse(writer, $"Program {requestedProgram.ProgramName} is not configured correctly. Please check the registry settings.", HttpStatusCode.BadRequest);
                }
                else if (didRun == "NotRunning")
                {
                    SendResponse(writer, $"Program {requestedProgram.ProgramName} did not start successfully. Please check the registry settings.", HttpStatusCode.InternalServerError);
                }
                else
                {
                    SendResponse(writer, $"Executed {requestedProgram.ProgramName} successfully.", HttpStatusCode.OK, true);  // Here we set the closeBrowser parameter to true.
                }
            }
            else if (request.Contains("GET /config"))
            {
                Log("Received request for config.");
                var configHtml = GenerateConfigHtml();
                SendResponse(writer, configHtml);
            }
            else if (request.Contains("GET /favicon.ico"))
            {
                Log("Received request for favicon.ico.");

                // Base64 encoded Letter L icon
                const string base64EncodedPng = "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAQklEQVR4nGP8////fwYKABMlmoeJASwwBiMjI1yQlHDFcAGpkTLwYYDVAEZGRpQwIckAYjXCADwWyE3RgzQQ6WoAALCIDSMEVNcoAAAAAElFTkSuQmCC";
                var imageBytes = Convert.FromBase64String(base64EncodedPng);
    
                SendImageResponse(writer, imageBytes);
            }
            else if (requestPath is "/" or "/index.html")
            {
                Log("Received request for / or index.html.");
                SendDefaultResponse(writer);
            }
            else
            {
                const string responseString = "Invalid request path. Either the program is not configured or the path is invalid.";
                SendResponse(writer, responseString, HttpStatusCode.BadRequest);
            }
            
            await writer.FlushAsync();

            // Close the client connection
            sslStream.Close();
            client.Close();
        }

        private string ExecuteAndMonitorProgram(ConfiguredProgram program)
        {
            if (!program.IsValid)
            {
                return "NotValid";
            }

            Log($"Executing command {program.CommandToRun}...");
            // if (program.CommandToRun != null) Process.Start(program.CommandToRun);
            Process? proc = null;
            if (program.CommandToRun != null) 
            {
                proc = Process.Start(program.CommandToRun);
                if (proc != null)
                {
                    proc.WaitForInputIdle(); // Wait for the process to be ready for user input
                    BringProcessToFront(proc);
                }
            }
            // Ensure the process name doesn't have an extension for comparison
            var processNameToVerify = Path.GetFileNameWithoutExtension(program.ProcessToVerify);

            Log($"Waiting for process {processNameToVerify} to start...");

            var counter = 0;


            while (counter < 3)
            {
                var allProcesses = Process.GetProcessesByName(processNameToVerify);

                if (allProcesses.Length > 0)
                {
                    Log($"Process {processNameToVerify} is running");
                    break; 
                }

                if (counter == 1)
                {
                    Log("Retrying command...");
                    if (program.CommandToRun != null) 
                    {
                        proc = Process.Start(program.CommandToRun);
                        if (proc != null)
                        {
                            proc.WaitForInputIdle();
                            BringProcessToFront(proc);
                        }
                    }
                }

                if (counter == 2)
                {
                    Log($"Process {processNameToVerify} is not running after 3 tries.");
                    return "NotRunning";
                }

                Thread.Sleep(2000);
                counter++;
            }

            return "OK";
        }

        
        private async void StartSslListener()
        {
            var hostName = (string?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\LHLauncher", "hostName", null);

            if (string.IsNullOrEmpty(hostName))
            {
                // check if its localhost
                serverCertificate = LoadCertificateFromStore("localhost");
                if (serverCertificate is null)
                {
                    // check if its localhost.com
                    serverCertificate = LoadCertificateFromStore("localhost.com");
                }
            }
            else
            {
                serverCertificate = LoadCertificateFromStore(hostName);
            }

            
            if (serverCertificate == null)
            {
                Console.WriteLine("Certificate not found. Exiting...");
                return;
            }
            var port = 443;
            // Listen on Localhost only
            var address = IPAddress.Parse("127.0.0.1");
            TcpListener listener = new TcpListener(address, port);
            listener.Start();
            Log($"Server Listening on {address} on port {port}...");
            Console.WriteLine($"Server Listening on {address} on port {port}...");

            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                _ = HandleSecureClient(client);  // Fire-and-forget. Don't await it.
            }
        }

        
        private static X509Certificate2? LoadCertificateFromStore(string subjectName)
        {
            X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);

            // Find the certificate with the specified subject name
            X509Certificate2Collection certCollection = store.Certificates.Find(X509FindType.FindBySubjectName, subjectName, false);
            store.Close();

            if (certCollection.Count > 0)
            {
                return certCollection[0]; // Return the first certificate, if found
            }
            return null; // Or handle this more gracefully

        }

        
    }
}
