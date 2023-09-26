$logFile = Join-Path $env:TEMP "LHLauncherLog.txt"

# Function to write to the log
function Write-Log {
    param (
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline = $true)]
        [string]$Message
    )
    Add-Content -Path $logFile -Value "$(Get-Date) - $Message"
}

Write-Log "Starting script..."

$taskName = "LaunchLHLauncher"
$launcherPath = Join-Path $PSScriptRoot "LHLauncher.exe"

Write-Log "Creating scheduled task action..."
$Action = New-ScheduledTaskAction -Execute $launcherPath
if (-not $?) { Write-Log "Error creating action: $_" }

Write-Log "Creating scheduled task trigger..."
$Trigger = New-ScheduledTaskTrigger -At (Get-Date) -Once
if (-not $?) { Write-Log "Error creating trigger: $_" }

# Fetch the username of the currently logged-in user using 'query user'
$User = query user 2>&1 | ForEach-Object { if ($_ -match '^\s*\d+\s+([^\s]+)\s+.*console') { $matches[1] } }
# Attempt to get the logged-in user
try {
    $User = (query user | Where-Object { $_ -match '>.*' } | ForEach-Object { ($_ -split '\s+')[0] }).TrimEnd()
    # Remove teh > from the username
    $User = $User.Substring(1)
    if (-not $User) {
        Write-Log "Failed to get the logged-in user."
        exit 1
    }
} catch {
    Write-Log "Error getting the logged-in user: $_"
    exit 1
}

# Check if we got the user
if (-not $User) {
    Write-Log "Failed to get the logged-in user."
    exit
}

$Principal = New-ScheduledTaskPrincipal -UserId $User -LogonType Interactive


Write-Log "Registering scheduled task..."
Register-ScheduledTask -TaskName $taskName -Action $Action -Trigger $Trigger -Principal $principal
if (-not $?) { Write-Log "Error registering task: $_" }

Write-Log "Starting scheduled task..."
Start-ScheduledTask -TaskName $taskName
if (-not $?) { Write-Log "Error starting task: $_" }

Start-Sleep -Seconds 3

Write-Log "Unregistering scheduled task..."
Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
if (-not $?) { Write-Log "Error unregistering task: $_" }

Write-Log "Script finished."

# # Pause for user input
# Read-Host "Press Enter to exit..."
