$exePath = Join-Path $PSScriptRoot "bin\Debug\net9.0-windows\DesktopCalendar.exe"

if (-not (Test-Path $exePath)) {
    dotnet build (Join-Path $PSScriptRoot "DesktopCalendar.csproj") | Out-Host
}

Start-Process -FilePath $exePath -WorkingDirectory $PSScriptRoot
