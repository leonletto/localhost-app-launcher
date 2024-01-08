 # PowerShell Script to Add Entry to Windows Hosts File

$hostsPath = "C:\Windows\System32\drivers\etc\hosts"
$entry = "127.0.0.1 localhost.com"

# Function to check if the entry already exists in the hosts file
function EntryExists {
    param ([string]$path, [string]$entry)

    $content = Get-Content -Path $path -ErrorAction SilentlyContinue
    $content -contains $entry
}

# Check if the hosts file contains the entry
if (-not (EntryExists -path $hostsPath -entry $entry)) {
    try {
        # Try to append the entry to the hosts file
        Add-Content -Path $hostsPath -Value $entry -ErrorAction Stop
        Write-Host "Entry '$entry' added to hosts file."
    } catch {
        # Handle potential errors, such as lack of permissions
        Write-Error "Failed to add entry to hosts file. Error: $_"
    }
} else {
    Write-Host "Entry '$entry' already exists in hosts file."
}
