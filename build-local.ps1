$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$env:TEMP = Join-Path $root '.tmp'
$env:TMP = $env:TEMP
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet'
$env:NUGET_PACKAGES = Join-Path $root '.nuget\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $root '.nuget\http-cache'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$publish = Join-Path $root '.build\publish\win-x64-v0.3.1'
if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
New-Item -ItemType Directory -Force -Path $env:TEMP, $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES, $env:NUGET_HTTP_CACHE_PATH, $publish | Out-Null
dotnet publish (Join-Path $root 'orbot-pc.csproj') --configuration Release --runtime win-x64 --self-contained true --output $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
$docs = Join-Path $publish 'docs'
New-Item -ItemType Directory -Force -Path $docs | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $docs 'OrbotPC-README.md') -Force
Copy-Item -LiteralPath (Join-Path $root 'BUILD_INFO.md'), (Join-Path $root 'THIRD_PARTY_NOTICES.md'), (Join-Path $root 'PORTING_STATUS.md') -Destination $docs -Force
$releaseDirectory = Join-Path $root '.build\release'
New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null
$archive = Join-Path $releaseDirectory 'OrbotPC-0.3.1-win-x64-portable.zip'
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($archive, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $publishRoot = (Resolve-Path -LiteralPath $publish).Path.TrimEnd([char]'\') + '\'
    Get-ChildItem -LiteralPath $publish -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($publishRoot.Length).Replace('\', '/')
        if ($relative -match '^(\.data|logs?)(/|$)' -or $_.Extension -eq '.pdb') { return }
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $_.FullName, $relative, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally { $zip.Dispose() }
Write-Host "Published to $publish"
Write-Host "Portable archive: $archive"
