# Load the XML file
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
[xml]$wixContent = Get-Content "$scriptDir\YourGeneratedComponents.wxs"

# Function to recursively remove directories and their components
function RemoveDirectoriesAndComponents($directory) {
#    Write-Host "Processing Directory: Id: $($directory.Id | Out-String) Name: $($directory.Name | Out-String)"
    # if the Directory Id is not INSTALLFOLDER, remove it
    if ($directory.Id -ne "INSTALLFOLDER")
    {
#        Write-Host "Removing Directory: $($directory.Id)"
        # Remove components in the directory
        foreach ($comp in $directory.Component)
        {
            $compId = $comp.Id
#            Write-Host "Processing Component: $compId"

            # Find and remove corresponding ComponentRef in the ComponentGroup
            $compRef = $wixContent.Wix.Fragment.ComponentGroup.ComponentRef | Where-Object { $_.Id -eq $compId }
            $componentDetails = $compRef | Select-Object -ExpandProperty OuterXml
#            Write-Host "ComponentRef: $componentDetails"
            if ($null -ne $compRef)
            {
                # remove the component from the ComponentGroup
                $compRef.ParentNode.RemoveChild($compRef) | Out-Null
#                Write-Host "Removed ComponentRef: $compId"
            }
        }
    }

    # Recursively process subdirectories
    foreach ($subDir in $directory.Directory) {
        RemoveDirectoriesAndComponents $subDir
    }

    if ($directory.Id -ne "INSTALLFOLDER")
    {
        # Remove the directory itself
#        Write-Host "Removing Directory: $( $directory.Id )"
        $directory.ParentNode.RemoveChild($directory) | Out-Null
    }
}

# Start processing from INSTALLFOLDER
$installFolder = $wixContent.Wix.Fragment.DirectoryRef
RemoveDirectoriesAndComponents $installFolder

# Save the modified XML back to the file
$wixContent.Save("$scriptDir\YourModifiedComponents.wxs")

function Convert-XmlToString([xml]$xml, [int]$indentation) {
    $stringWriter = New-Object System.IO.StringWriter
    $xmlTextWriter = New-Object System.Xml.XmlTextWriter($stringWriter)
    $xmlTextWriter.Formatting = 'Indented'
    $xmlTextWriter.Indentation = $indentation
    $xmlTextWriter.IndentChar = ' '
    $xml.WriteContentTo($xmlTextWriter)
    $xmlTextWriter.Flush()
    $stringWriter.Flush()
    return $stringWriter.ToString()
}

# Load the XML files
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
[xml]$generatedXml = Get-Content "$scriptDir\YourModifiedComponents.wxs"
[xml]$mainXml = Get-Content "$scriptDir\LHLauncher.wxs"

# Find the INSTALLFOLDER in the main WXS file
$installFolderInMainXml = $mainXml.Wix.Product.Directory.Directory.Directory | Where-Object { $_.Id -eq "INSTALLFOLDER" }

# Extract components from generated WXS file
$generatedComponents = $generatedXml.Wix.Fragment.DirectoryRef.Component

# Ensure all components are referenced in the ComponentGroup
$componentGroup = $mainXml.Wix.Product.ComponentGroup | Where-Object { $_.Id -eq "ProductComponents" }
$existingComponentRefs = $componentGroup.ComponentRef | Select-Object -ExpandProperty Id

foreach ($component in $generatedComponents) {
    $compId = $component.Id
    $compSource = $component.File.Source
    $existingComponent = $installFolderInMainXml.Component | Where-Object { $_.File.Source -eq $compSource }

    # Add component to INSTALLFOLDER if it doesn't exist
    if ($null -eq $existingComponent) {
        $newComponent = $mainXml.ImportNode($component, $true)
        $installFolderInMainXml.AppendChild($newComponent) | Out-Null
        
    }

    # Add ComponentRef to ComponentGroup if it doesn't exist
    if ($compId -notin $existingComponentRefs -and $null -eq $existingComponent) {
        $newComponentRef = $mainXml.CreateElement("ComponentRef", $mainXml.Wix.NamespaceURI)
        $newComponentRef.SetAttribute("Id", $compId)
        $componentGroup.AppendChild($newComponentRef) | Out-Null
    }
}

# Convert the XML to a string with four spaces indentation
$xmlString = Convert-XmlToString -xml $mainXml -indentation 4

## Save the modified main WXS file
$xmlString | Set-Content "$scriptDir\UpdatedLHLauncher.wxs"
