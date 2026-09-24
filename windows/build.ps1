<#
.SYNOPSIS
  Build, test, package and install After Dork for Windows.

.EXAMPLE
  .\build.ps1 -Test              # unit tests
  .\build.ps1 -Publish           # dist\AfterDork\ (AfterDork.exe + one .scr per module) and a zip
  .\build.ps1 -Install           # publish, then install to %LOCALAPPDATA%\Programs\AfterDork + Start Menu shortcut
  .\build.ps1 -Previews          # render PNG frames of every saver into out\previews (like `make previews`)
#>
param(
    [switch]$Test,
    [switch]$Publish,
    [switch]$Install,
    [switch]$Previews,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$modules = 'FlyingFlasks', 'GlasswarePipes', 'LatticeMaze', 'MystifyPolymers',
           'StoddartReef', 'OrbitalBox', 'SmilesRain', 'CastawayChemist'
$dist = Join-Path $root 'dist\AfterDork'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\AfterDork'

function Invoke-Dotnet {
    & $dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed ($LASTEXITCODE)" }
}

if (-not ($Test -or $Publish -or $Install -or $Previews)) { $Test = $true; $Publish = $true }

if ($Test) {
    Invoke-Dotnet test (Join-Path $root 'tests\AfterDork.Tests') -c $Configuration
}

function Publish-Variant([string]$out, [bool]$selfContained) {
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    Invoke-Dotnet publish (Join-Path $root 'src\AfterDork') -c $Configuration -o $out -r win-x64 `
        --self-contained $selfContained.ToString().ToLower() -p:DebugType=None -p:SatelliteResourceLanguages=en
    # Native debug symbols (libSkiaSharp.pdb is 80 MB) have no place in a download.
    Get-ChildItem $out -Recurse -Include *.pdb, Microsoft.DiaSymReader.Native.*.dll | Remove-Item -Force
    Copy-Item (Join-Path $root 'dist-readme.txt') (Join-Path $out 'README.txt')
    # Each .scr is a copy of the launcher: it starts AfterDork.dll beside it,
    # and its file name selects the module.
    foreach ($m in $modules) { Copy-Item (Join-Path $out 'AfterDork.exe') (Join-Path $out "$m.scr") -Force }
}

function New-Zip([string]$folder, [string]$zip) {
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path "$folder\*" -DestinationPath $zip -CompressionLevel Optimal
    Write-Host ("  {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
}

if ($Publish -or $Install) {
    # Self-contained: works on any 64-bit Windows 10/11 PC, nothing else to install.
    Publish-Variant $dist $true
    $version = (Get-Item (Join-Path $dist 'AfterDork.dll')).VersionInfo.ProductVersion -replace '\+.*$', ''
    Write-Host "Published $dist"
    New-Zip $dist (Join-Path $root "dist\AfterDork-$version-Windows.zip")
    if ($Publish) {
        # Small variant for PCs that already have the .NET 8 Desktop Runtime.
        $small = Join-Path $root 'dist\AfterDork-small'
        Publish-Variant $small $false
        New-Zip $small (Join-Path $root "dist\AfterDork-$version-Windows-small-needs-dotnet8.zip")
    }
}

if ($Install) {
    New-Item -ItemType Directory -Force $installDir | Out-Null
    Get-Process AfterDork -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$installDir*" } | Stop-Process -Force
    Copy-Item "$dist\*" $installDir -Recurse -Force
    $startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'After Dork.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($startMenu)
    $lnk.TargetPath = Join-Path $installDir 'AfterDork.exe'
    $lnk.WorkingDirectory = $installDir
    $lnk.Description = 'Chemistry screen savers'
    $lnk.Save()
    Write-Host "Installed to $installDir (Start Menu: After Dork)"
}

if ($Previews) {
    $cli = Join-Path $root 'tools\AfterDork.Cli\bin\Release\net8.0-windows\afterdork-cli.exe'
    Invoke-Dotnet build (Join-Path $root 'tools\AfterDork.Cli') -c Release
    $out = Join-Path $root 'out\previews'
    New-Item -ItemType Directory -Force $out | Out-Null
    foreach ($m in $modules) { & $cli render $m 1280 720 240 (Join-Path $out $m) }
}
