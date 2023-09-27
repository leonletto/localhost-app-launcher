# Function to log errors
function LogError($message) {
    Write-Output "Error: $message"
}

try {
    # Check if the base registry key exists; if not, create it.
    $baseKeyPath = "HKCU:\SOFTWARE\LHLauncher"
    if (-not (Test-Path $baseKeyPath)) {
        New-Item -Path $baseKeyPath -Force
    }

    # Define a list of app details
    $apps = @(
        @{
            Name        = "calculator"
            Command     = '"C:\Windows\System32\calc.exe"'
            ProcessName = '"CalculatorApp"'
        },
        @{
            Name        = "notepad"
            Command     = '"C:\Windows\System32\Notepad.exe"'
            ProcessName = '"notepad"'
        }
    )

    # Iterate through the list and add the registry entries
    foreach ($app in $apps) {
        $appKeyPath = "$baseKeyPath\$($app.Name)"
        if (-not (Test-Path $appKeyPath)) {
            New-Item -Path $appKeyPath -Force
        }

        Set-ItemProperty -Path $appKeyPath -Name "Command" -Value $app.Command
        Set-ItemProperty -Path $appKeyPath -Name "ProcessName" -Value $app.ProcessName
    }

    # Set the 'loggingPath' value for the base key
    Set-ItemProperty -Path $baseKeyPath -Name "loggingPath" -Value ""

    # Set the 'ProductVersion' value for the base key
    Set-ItemProperty -Path $baseKeyPath -Name "ProductVersion" -Value "1.0.7"
    
    # Set the url to the WS ONE Access App catalog for redirects
    Set-ItemProperty -Path $baseKeyPath -Name "appCatalogURL" -Value "https://ws-one-uem-651.workspaceair.com/catalog-portal/ui#/apps"
    
    # Define the path to the hosts file
	$hostsFilePath = "C:\Windows\System32\drivers\etc\hosts"

    # Define the hostname to be added
    $lineToAdd = "127.0.0.1       localhost localhost.com"

    # Check if the line already exists in the hosts file
    $lineExists = Get-Content -Path $hostsFilePath | Select-String -Pattern $lineToAdd -Quiet

    # If the line doesn't exist, append it to the file
    if (-Not $lineExists) {
        Add-Content -Path $hostsFilePath -Value $lineToAdd
    }
} catch {
    # Handle the error and log a descriptive message
    LogError $_.Exception.Message
}

Write-Output "Configured"