#Requires -Version 5.1
<#
.SYNOPSIS
    Builds AudioLeash and packages it as a Windows installer.

.DESCRIPTION
    1. Cleans .\publish\ to remove any stale files from previous builds.
    2. Runs `dotnet publish` to produce a framework-dependent build under
       .\publish\.
    3. Compiles installer\AudioLeash.iss with Inno Setup 6 to produce
       installer\Output\AudioLeash-Setup.exe.

.NOTES
    Prerequisites
    -------------
    .NET 8 SDK     https://dotnet.microsoft.com/download/dotnet/8
    Inno Setup 6   https://jrsoftware.org/isinfo.php
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ScriptDir  = $PSScriptRoot
$Project    = Join-Path $ScriptDir 'AudioLeash\AudioLeash.csproj'
$PublishDir = Join-Path $ScriptDir 'publish'
$IssFile    = Join-Path $ScriptDir 'installer\AudioLeash.iss'

# ---------------------------------------------------------------------------
# 1. Clean publish folder
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '==> Cleaning publish folder...' -ForegroundColor Cyan

if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}

# ---------------------------------------------------------------------------
# 2. dotnet publish
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '==> Publishing AudioLeash (framework-dependent)...' `
    -ForegroundColor Cyan

dotnet publish $Project `
    --configuration Release `
    --output $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error 'dotnet publish failed. See output above.'
    exit 1
}

# ---------------------------------------------------------------------------
# 3. Locate ISCC.exe (Inno Setup compiler)
# ---------------------------------------------------------------------------
$isccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)

$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Error (
        "Inno Setup 6 not found.`n" +
        "Install it from https://jrsoftware.org/isinfo.php and re-run this script."
    )
    exit 1
}

# ---------------------------------------------------------------------------
# 4. Compile installer
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '==> Compiling Inno Setup installer...' -ForegroundColor Cyan

& $iscc $IssFile

if ($LASTEXITCODE -ne 0) {
    Write-Error 'ISCC failed. See output above.'
    exit 1
}

# ---------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------
$output = Join-Path $ScriptDir 'installer\Output\AudioLeash-Setup.exe'
Write-Host ''
Write-Host "==> Done!  Installer: $output" -ForegroundColor Green
