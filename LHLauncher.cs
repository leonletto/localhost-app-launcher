using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Net.Security;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Bobs.Shell;
using Timer = System.Threading.Timer;


namespace LHLauncher
{
    public class ConfiguredProgram
    {
        public bool IsValid { get; init;}
        public string? ProgramName { get; init; }
        public string? ProcessToVerify { get; init; }
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
        
        private bool warningIssued = false;
        
        private static string GetAppCatalogUrl()
        {
            var appCatalogUrl = (string?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\LHLauncher", "appCatalogURL", null);

            if (!string.IsNullOrEmpty(appCatalogUrl))
                return appCatalogUrl;

            // Default to localhost.com
            var url = GetHostNameForCertificate();
            appCatalogUrl = "https://{url}".Replace("{url}", url + "/config");

            return appCatalogUrl;
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
        
        public enum LogLevel
        {
            Debug,
            Info
        }

        private LogLevel currentLogLevel = LogLevel.Info;
        private void Log(string message, LogLevel logLevel = LogLevel.Info)
        {
            // Only log messages that are of the current log level or more severe
            if (logLevel < currentLogLevel)
            {
                return;
            }
            // Get the current stack trace and the previous frame (caller)
            var frame = new StackTrace(1, true).GetFrame(0); // '1' skips one frame to get the caller of this method
            if (frame == null) return;
            var fileName = Path.GetFileNameWithoutExtension(frame.GetFileName());
            var lineNo = frame.GetFileLineNumber();

            var loggingPath = GetLoggingPath();
            var logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{fileName}:{lineNo}] {message}";
            // Console.WriteLine(logMessage);
            File.AppendAllText(loggingPath, logMessage + Environment.NewLine);
        }


        private readonly NotifyIcon _trayIcon;
        
        private X509Certificate2? _serverCertificate;
        private readonly List<ConfiguredProgram> _programs = new();
        
        // Add timer for reload
        private Timer _registryCheckTimer;
        private readonly object _programsLock = new object();
        
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
            public MOUSE_INPUT mi;
        }

        private struct MOUSE_INPUT
        {
            public int Dx;
            public int Dy;
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
            public int MouseData;
            public int DwFlags;
            public int Time;
            public IntPtr DwExtraInfo;
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value
        }

        private void SimulateUserInput()
        {
            var input = new INPUT();
            input.type = 0; // MOUSE input
            input.mi.Dx = 0;
            input.mi.Dy = 0;
            input.mi.DwFlags = 0x1; // MOUSEEVENTF_MOVE

            SendInput(1, ref input, Marshal.SizeOf(typeof(INPUT)));
        }

        private void BringProcessToFront(Process process)
        {
            if (process.MainWindowHandle == IntPtr.Zero) return; // Process doesn't have a main window

            ShowWindow(process.MainWindowHandle, SW_RESTORE);
            SimulateUserInput();
            SetForegroundWindow(process.MainWindowHandle);
        }
        
        private void RegistryCheckCallback(object? state = null)
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
            _registryCheckTimer = new Timer(RegistryCheckCallback, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));

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

            try
            {
                var newLogLevel = baseRegistryKey.GetValue("logLevel", null);
                if (newLogLevel != null)
                {
                    if (newLogLevel.ToString() == "Debug")
                    {
                        currentLogLevel = LogLevel.Debug;
                    }
                    else
                    {
                        currentLogLevel = LogLevel.Info;
                    }
                }
            }
            catch (Exception e)
            {
                currentLogLevel = LogLevel.Info;
                Log($"LogLevel not set in the registry, defaulting to {currentLogLevel}. Error: {e.Message}", LogLevel.Debug);
            }

            lock (_programsLock)
            {
                foreach (var programName in subKeyNames)
                {
                    using var subKey = baseRegistryKey.OpenSubKey(programName);
                    if (subKey == null) continue;
                    var isBadString = false;

                    var commandString = (string?)subKey.GetValue("Command", null);
                    if (commandString != null)
                    {
                        commandString = CheckIfLinkAndParse(commandString);
                        Log($"commandToRun: {commandString}", LogLevel.Debug);
                        var (executable, arguments) = ParseCommand(commandString);
                        Log($"executable: {executable} arguments: {arguments}", LogLevel.Debug);
                        
                        if (string.IsNullOrEmpty(executable) || !System.IO.File.Exists(executable))
                        {
                            if (!warningIssued) Log($"Executable for {commandString} does not exist");
                            isBadString = true;
                            warningIssued = true;
                        }
                    }
                    
                    var processToVerifyTemp = (string?)subKey.GetValue("ProcessName", null);
                    processToVerifyTemp = processToVerifyTemp?.Trim('"').ToLower();
                    var program = new ConfiguredProgram
                    {
                        ProgramName = programName,
                        CommandToRun = (string?)subKey.GetValue("Command", null),
                        ProcessToVerify = processToVerifyTemp,
                        IsValid = !string.IsNullOrEmpty((string?)subKey.GetValue("Command", null)) &&
                                  !string.IsNullOrEmpty((string?)subKey.GetValue("ProcessName", null)) &&
                                  !isBadString
                    };
                    _programs.RemoveAll(p => p.ProgramName == programName);
                    _programs.Add(program);
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
            htmlBuilder.Append($"<h2>App Catalog: <a href='{GetAppCatalogUrl()}'>{GetAppCatalogUrl()}</a></h2>");
            htmlBuilder.Append("<h2>Currently Configured LHLauncher Applications</h2>");
            htmlBuilder.Append("<table border='1'><thead><tr><th>Name</th><th>Process Name</th><th>Process to Launch</th><th>Test URL</th></tr></thead><tbody>");

            foreach (ConfiguredProgram program in _programs)
            {
                if (program.IsValid)
                {
                    var urlToOpen = "https://" + GetHostNameForCertificate() + "/" + program.ProgramName;
                    htmlBuilder.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td><a href='javascript:void(0)' onclick='openInNewTab(\"{3}\")'>Open {0}</a></td></tr>", program.ProgramName, program.ProcessToVerify, program.CommandToRun, urlToOpen);
                }
                
            }
            // foreach (string programName in baseRegistryKey.GetSubKeyNames())
            // {
            //     using (var subkey = baseRegistryKey.OpenSubKey(programName))
            //     {
            //         if (subkey != null)
            //         {
            //             var commandToRun = (string?)subkey.GetValue("Command", null);
            //             var processToVerify = (string?)subkey.GetValue("ProcessName", null);
            //             if (string.IsNullOrEmpty(commandToRun) || string.IsNullOrEmpty(processToVerify)) continue;
            //             var urlToOpen = "https://" + GetHostNameForCertificate() + "/" + programName;
            //             htmlBuilder.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td><a href='javascript:void(0)' onclick='openInNewTab(\"{3}\")'>Open {0}</a></td></tr>", programName, processToVerify, commandToRun, urlToOpen);
            //         }
            //     }
            // }
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
            const string RESPONSE_STRING = "<html><head><title>LHLauncher</title></head><body><h1>LHLauncher is listening...</h1></body></html>";
            SendResponse(writer, RESPONSE_STRING);
        }
        
        private static void SendResponse(TextWriter writer, string responseString = "OK", HttpStatusCode statusCode = HttpStatusCode.OK, bool closeBrowser = false)
        {
            if (closeBrowser)
            {
               var appCatalogUrl = GetAppCatalogUrl();
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
            if (_serverCertificate != null) sslStream.AuthenticateAsServer(_serverCertificate); else
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
            const string PATH_PATTERN = @"^(/[\w\-\.]*|/?)$";
            var match = Regex.Match(requestPath ?? string.Empty, PATH_PATTERN);

            if (!match.Success)
            {
                Log($"Invalid request path. {requestPath}", LogLevel.Debug);
                SendResponse(writer, "Invalid request path.", HttpStatusCode.BadRequest);
                return;  // Ensure you exit out of the function after sending an error response.
            }  

            // Check request length
            if (request.Length > 2048) 
            {
                Log("Request too long. {requestPath}", LogLevel.Debug);
                SendResponse(writer, "Request too long.", HttpStatusCode.RequestEntityTooLarge);
            }

            // ReSharper disable once InconsistentlySynchronizedField
            var requestedProgram = _programs.FirstOrDefault(p => requestPath != null && requestPath.Equals($"/{p.ProgramName}"));

            if (requestedProgram != null)
            {
                var didRun = await ExecuteAndMonitorProgramAsync(requestedProgram);
                switch (didRun)
                {
                    case "NotValid":
                        SendResponse(writer, $"Program {requestedProgram.ProgramName} is not configured correctly. Please check the registry settings.", HttpStatusCode.BadRequest);
                        break;
                    case "NotRunning":
                        SendResponse(writer, $"Program {requestedProgram.ProgramName} did not start successfully. Please check the registry settings.", HttpStatusCode.InternalServerError);
                        break;
                    case "OK":
                        SendResponse(writer, $"Executed {requestedProgram.ProgramName} successfully.", HttpStatusCode.OK, true); // Here we set the closeBrowser parameter to true.
                        break;
                    default:
                        SendResponse(writer, $"There was an error when running {requestedProgram.ProgramName}.<br/> Error: {didRun}", HttpStatusCode.InternalServerError); 
                        break;
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
                Log("Received request for favicon.ico.", LogLevel.Debug);

                // Base64 encoded Letter L icon
                const string BASE64_ENCODED_PNG = "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAQklEQVR4nGP8////fwYKABMlmoeJASwwBiMjI1yQlHDFcAGpkTLwYYDVAEZGRpQwIckAYjXCADwWyE3RgzQQ6WoAALCIDSMEVNcoAAAAAElFTkSuQmCC";
                var imageBytes = Convert.FromBase64String(BASE64_ENCODED_PNG);
    
                SendImageResponse(writer, imageBytes);
            }
            else if (requestPath is "/" or "/index.html")
            {
                Log("Received request for / or index.html." , LogLevel.Debug);
                SendDefaultResponse(writer);
            }
            else
            {
                const string RESPONSE_STRING = "Invalid request path. Either the program is not configured or the path is invalid.";
                SendResponse(writer, RESPONSE_STRING, HttpStatusCode.BadRequest);
            }
            
            await writer.FlushAsync();

            // Close the client connection
            sslStream.Close();
            client.Close();
        }
        
        private (string? Executable, string? Arguments) ParseCommand(string? command)
        {
            Log($"Starting to parse command: {command}", LogLevel.Debug);

            if (string.IsNullOrEmpty(command))
            {
                Log("Command is null or empty.", LogLevel.Debug);
                return (null, null);
            }

            // Count the number of quotes at the beginning and end of the string
            int startQuotes = command.StartsWith("\"\"") ? 2 : (command.StartsWith("\"") ? 1 : 0);
            int endQuotes = command.EndsWith("\"\"") ? 2 : (command.EndsWith("\"") ? 1 : 0);
            
            // Remove outer quotes only if both sides have a single quote, indicating the whole command is enclosed
            // unless there is a % after the quote
            if (startQuotes == 1 && endQuotes == 1)
            {
                    command = command[1..^1];
            }
            
            // Also remove quotes if both sides have double quotes, indicating the whole command is enclosed
            else if (startQuotes == 2 && endQuotes == 2)
            {
                command = command[1..^1];
            }
            
            // Also remove quotes if the start has 2 and the end has one, indicating the executable is enclosed
            else if (startQuotes == 2 && endQuotes == 1)
            {
                command = command[1..];
            }

            Log($"Command after handling outer quotes: {command}", LogLevel.Debug);

            // Initialize string builders to hold the executable and the arguments
            StringBuilder executable = new StringBuilder();
            StringBuilder arguments = new StringBuilder();

            // Flags to indicate the current parsing context
            bool insideQuotes = false;
            bool executableParsed = false;


            for (int i = 0; i < command.Length; i++)
            {
                char c = command[i];

                if (c == '"')
                {
                    insideQuotes = !insideQuotes;
                    continue;  // Skip the quote character itself
                }

                if (c == ' ' && !insideQuotes)
                {
                    if (!executableParsed)
                    {
                        // First unquoted space indicates the separation between the executable and the arguments
                        executableParsed = true;
                    }
                    else
                    {
                        // Additional spaces are considered part of the arguments
                        arguments.Append(c);
                    }
                }
                else if (c == ' ' && !executableParsed)
                {
                        executable.Append(c);
                }
                else
                {
                    if (executableParsed)
                    {
                        // Append characters to arguments after the executable has been parsed
                        arguments.Append(c);
                    }
                    else
                    {
                        // Append characters to executable until the first unquoted space is found
                        executable.Append(c);
                    }
                }
            }

            Log($"Executable parsed: {executable}", LogLevel.Debug);
            Log($"Arguments parsed: {arguments}", LogLevel.Debug);

            return (executable.ToString(), arguments.ToString());
        }
        
        private string CheckIfLinkAndParse(string commandToRun)
        {
            Log($"Checking if {commandToRun} is a .lnk file...", LogLevel.Debug);
                
            if (Path.GetExtension(commandToRun).ToLower() == ".lnk" || Path.GetExtension(commandToRun[1..^1]).ToLower() == ".lnk")
            {
                try 
                {
                    Log($"Attempting to resolve .lnk file {commandToRun}", LogLevel.Debug);
                    ShellLink link;
                    if (Path.GetExtension(commandToRun).ToLower() == ".lnk")
                    {
                        // check if the file exists
                        if (!File.Exists(commandToRun))
                        {
                            Log($"File {commandToRun} does not exist.", LogLevel.Debug);
                            return commandToRun;
                        }
                        link = new ShellLink(commandToRun);
                    }
                    else
                    {
                        // check if the file exists
                        if (!File.Exists(commandToRun[1..^1]))
                        {
                            Log($"File {commandToRun[1..^1]} does not exist.", LogLevel.Debug);
                            return commandToRun;
                        }
                        link = new ShellLink(commandToRun[1..^1]);
                    }

                    commandToRun = $"\"{link.Target}\" {link.Arguments}";
                    Log($"New command to run fom Link: {commandToRun}", LogLevel.Debug);
                }
                catch (Exception ex) 
                {
                    Log($"Error while processing the .lnk file: {ex.Message}");
                    return $"There was an error while processing the .lnk file: {ex.Message}";
                }
            }

            return commandToRun;
        }




        private async Task<string> ExecuteAndMonitorProgramAsync(ConfiguredProgram program, CancellationToken cancellationToken = default)
        {
            Log($"Checking if program {program.ProgramName} is valid - {program.IsValid}", LogLevel.Debug);
            if (!program.IsValid)
            {
                Log($"Program {program.ProgramName} is not valid.");
                Log($"Program Details: CommandToRun: {program.CommandToRun}, ProcessToVerify: {program.ProcessToVerify}", LogLevel.Debug);
                return "NotValid";
            }

            Log($"Executing command {program.CommandToRun}...");
            Process? proc = null;
            string? commandToRun = program.CommandToRun;
            
            if (commandToRun != null)
            {
                
                commandToRun = CheckIfLinkAndParse(commandToRun);
                
                Log($"Going to Parse command {commandToRun}...", LogLevel.Debug);
                var (executable, arguments) = ParseCommand(commandToRun);
                Log($"Executable: {executable}, Arguments: {arguments}", LogLevel.Debug);
                
                // Start the process
                if (executable != null)
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo(executable, arguments??"");
                    Log($"Starting process {executable} with arguments {arguments}", LogLevel.Debug);
                    proc = Process.Start(startInfo);
                    Log($"Process started: {proc?.Id}", LogLevel.Debug);
                    if (proc != null)
                    {
                        Log($"Waiting for process {proc.Id} to start...", LogLevel.Debug);
                        await Task.Run(() => proc.WaitForInputIdle(), cancellationToken);
                        Log($"Process {proc.Id} is now idle.", LogLevel.Debug);
                        BringProcessToFront(proc);
                    }
                }
                
            }

            var processNameToVerify = Path.GetFileNameWithoutExtension(program.ProcessToVerify);
            Log($"Waiting for process {processNameToVerify} to start...", LogLevel.Debug);
            var counter = 0;

            while (counter < 3)
            {
                var allProcesses = await Task.Run(() => Process.GetProcessesByName(processNameToVerify), cancellationToken);
                if (allProcesses.Length > 0)
                {
                    Log($"Process {processNameToVerify} is running");
                    break;
                }

                switch (counter)
                {
                    case 1:
                        Log("Retrying command...", LogLevel.Debug);
                        var (executable, arguments) = ParseCommand(commandToRun);
                
                        // Start the process
                        if (executable != null)
                        {
                            ProcessStartInfo startInfo = new ProcessStartInfo(executable, arguments??"");
                            proc = Process.Start(startInfo);
                            if (proc != null)
                            {
                                proc.WaitForInputIdle();
                                BringProcessToFront(proc);
                            }
                        }
                        break;
                    case 2:
                        Log($"Process {processNameToVerify} is not running after 3 tries.");
                        return "NotRunning";
                }

                await Task.Delay(2000, cancellationToken);
                counter++;
            }

            return "OK";
        }


        
        private async void StartSslListener()
        {
            var hostName = (string?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\LHLauncher", "hostName", null);
            Log($"HostName: {hostName} if null, will try localhost.com", LogLevel.Debug);
            if (string.IsNullOrEmpty(hostName))
            {
                // check if its localhost
                _serverCertificate = LoadCertificateFromStore("localhost");
                if (_serverCertificate is null)
                {
                    // check if its localhost.com
                    _serverCertificate = LoadCertificateFromStore("localhost.com");
                }
            }
            else
            {
                _serverCertificate = LoadCertificateFromStore(hostName);
            }
            Log($"Server Certificate Thumbprint : {_serverCertificate?.Thumbprint}", LogLevel.Debug);
            Log($"Server Certificate Subject: {_serverCertificate?.Subject}", LogLevel.Debug);

            
            if (_serverCertificate == null)
            {
                Console.WriteLine("Certificate not found. Exiting...");
                Log("Certificate not found. Exiting...");
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

        
        private X509Certificate2? LoadCertificateFromStore(string subjectName)
        {
            X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);

            // Find the certificate with the specified subject name
            X509Certificate2Collection certCollection = store.Certificates.Find(X509FindType.FindBySubjectName, subjectName, false);
            store.Close();

            if (certCollection.Count > 0)
            {
                // Return the first certificate, if found
                X509Certificate2 cert = certCollection[0];

                // Check for expiration
                if (DateTime.Now > cert.NotAfter || DateTime.Now < cert.NotBefore)
                {
                    Log("The certificate is expired or not yet valid.");
                    return null;
                }

                // Build the certificate chain
                X509Chain chain = new X509Chain();

                // Enable CRL checking
                chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                
                bool chainBuilt = chain.Build(cert);

                if (!chainBuilt)
                {
                    bool crlNotFound = chain.ChainStatus.Any(status => status.Status == X509ChainStatusFlags.RevocationStatusUnknown);
                    bool certificateRevoked = chain.ChainStatus.Any(status => status.Status == X509ChainStatusFlags.Revoked);
    
                    if (crlNotFound)
                    {
                        Log("CRL not found. Proceeding anyway, but this should be investigated.");
                    }
    
                    if (certificateRevoked)
                    {
                        Log("The certificate has been revoked.");
                        return null;
                    }
    
                    if (!crlNotFound)
                    {
                        Log("The certificate chain could not be built for unknown reasons.");
                        return null;
                    }
                }

                // Disallow self-signed certificates by checking the chain length
                if (chain.ChainElements.Count <= 1)
                {
                    Log("The certificate is self-signed, which is not allowed.");
                    return null;
                }

                // Check certificate purpose
                bool hasServerAuthEku = false;
                foreach (var extension in cert.Extensions)
                {
                    if (extension is X509EnhancedKeyUsageExtension eku)
                    {
                        var serverAuthOid = new Oid("1.3.6.1.5.5.7.3.1"); // OID for Server Authentication
                        foreach (Oid oid in eku.EnhancedKeyUsages)
                        {
                            if (oid.Value == serverAuthOid.Value)
                            {
                                hasServerAuthEku = true;
                                break;
                            }
                        }

                        if (!hasServerAuthEku)
                        {
                            Log("The certificate is not intended for SSL server authentication.");
                            return null;
                        }
                    }
                }

                // If it passed all the checks, return the certificate
                return cert;
            }

            Log("Certificate not found.");
            return null; // Or handle this more gracefully
        }



        
    }
}
