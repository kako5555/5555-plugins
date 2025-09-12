@echo off
echo Building AutoExtract plugin...
cd AutoExtract
dotnet build --configuration Release
if %ERRORLEVEL% EQU 0 (
    echo Build successful! Plugin DLL is in bin\Release\AutoExtract.dll
    echo Copy this DLL to your Dalamud devPlugins directory
) else (
    echo Build failed! Make sure you have .NET 9.0.9 runtime installed
    echo Download from: https://dotnet.microsoft.com/download/dotnet/9.0
)
pause