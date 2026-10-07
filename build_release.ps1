$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SystemTool Build & Obfuscate Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$projectPath = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $projectPath

Write-Host "`n[1/5] Cleaning previous build..." -ForegroundColor Yellow
Remove-Item -Path "bin\Release" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "obj\Release" -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "`n[2/5] Building Release version..." -ForegroundColor Yellow
dotnet build -c Release -r win-x64 --self-contained false --no-incremental
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "`n[3/5] Running Obfuscar..." -ForegroundColor Yellow
obfuscar.console obfuscar.xml
if ($LASTEXITCODE -ne 0) {
    Write-Host "Obfuscation failed!" -ForegroundColor Red
    exit 1
}

Write-Host "`n[4/5] Publishing single file..." -ForegroundColor Yellow
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:SelfContained=false -p:PublishSelfContained=false -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -p:DebugType=none -p:DebugSymbols=false --no-build
if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!" -ForegroundColor Red
    exit 1
}

Write-Host "`n[5/5] Replacing with obfuscated DLL..." -ForegroundColor Yellow
$publishPath = "bin\Release\net10.0-windows\win-x64\publish"
$obfuscatedDll = "bin\Release\net10.0-windows\obfuscated\SystemTool.dll"

if (Test-Path $obfuscatedDll) {
    Copy-Item -Path $obfuscatedDll -Destination "$publishPath\SystemTool.dll" -Force
    Write-Host "Obfuscated DLL copied successfully!" -ForegroundColor Green
} else {
    Write-Host "Warning: Obfuscated DLL not found, using original DLL" -ForegroundColor Yellow
}

$outputPath = Join-Path $projectPath $publishPath
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Build completed successfully!" -ForegroundColor Green
Write-Host "Output: $outputPath\SystemTool.exe" -ForegroundColor White
Write-Host "========================================" -ForegroundColor Cyan

Get-ChildItem -Path $publishPath -Filter "*.exe" | ForEach-Object {
    $size = [math]::Round($_.Length / 1MB, 2)
    Write-Host "File: $($_.Name) - Size: $size MB" -ForegroundColor Gray
}
