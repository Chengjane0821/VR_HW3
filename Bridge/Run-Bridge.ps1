param(
    [string]$SerialPort = 'COM5',
    [int]$BaudRate = 9600,
    [int]$TcpPort = 8888,
    [Parameter(Mandatory=$true)][string]$ListenIP
)
$ErrorActionPreference = 'Stop'
& dotnet run --project (Join-Path $PSScriptRoot 'ArduinoBridge.csproj') -c Release -- $SerialPort $BaudRate $TcpPort $ListenIP
exit $LASTEXITCODE
