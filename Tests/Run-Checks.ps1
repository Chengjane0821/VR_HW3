$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& dotnet build (Join-Path $root 'Bridge/ArduinoBridge.csproj') -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet run --project (Join-Path $root 'Bridge/ArduinoBridge.csproj') -c Release --no-build -- --self-test
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet run --project (Join-Path $PSScriptRoot 'TransportChecks/TransportChecks.csproj') -c Release
exit $LASTEXITCODE
