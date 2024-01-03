@echo off
SET WIX_PATH=c:\Program Files (x86)\WiX Toolset v3.11\bin
SET PATH=%WIX_PATH%;%PATH%

heat dir ".\bin\Release\net6.0-windows" -dr INSTALLFOLDER -cg GroupName -gg -srd -scom -sreg -sfrag -var var.ReleaseDir -out YourGeneratedComponents.wxs
powershell ./updateComponents.ps1

candle -dReleaseDir=".\bin\Release\net6.0-windows" UpdatedLHLauncher.wxs -ext WixUtilExtension 
light UpdatedLHLauncher.wixobj -ext WixUtilExtension -out bin\Release\LHLauncher.msi

del YourGeneratedComponents.wxs
del YourModifiedComponents.wxs
del UpdatedLHLauncher.wixobj
del UpdatedLHLauncher.wxs

