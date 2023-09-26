@echo off
SET WIX_PATH=c:\Program Files (x86)\WiX Toolset v3.11\bin
SET PATH=%WIX_PATH%;%PATH%

candle -dReleaseDir=".\bin\Release\net6.0-windows" LHLauncher.wxs -ext WixUtilExtension 
light LHLauncher.wixobj -ext WixUtilExtension -out bin\Release\LHLauncher.msi
