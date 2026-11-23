$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$env:TEMP = Join-Path $root '.tmp'
$env:TMP = $env:TEMP
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet'
$env:NUGET_PACKAGES = Join-Path $root '.nuget\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $root '.nuget\http-cache'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$publish = Join-Path $root '.build\publish\win-x64'
New-Item -ItemType Directory -Force -Path $env:TEMP, $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES, $env:NUGET_HTTP_CACHE_PATH, $publish | Out-Null
dotnet publish (Join-Path $root 'orbot-pc.csproj') --configuration Release --runtime win-x64 --self-contained true --output $publish
Write-Host "Published to $publish"
