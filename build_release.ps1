$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "系统工具箱 Build & Publish Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$projectPath = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $projectPath

Write-Host "`n[1/3] Cleaning previous build..." -ForegroundColor Yellow
Remove-Item -Path "bin\Release" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "obj\Release" -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "`n[2/3] Building Release version..." -ForegroundColor Yellow
dotnet build -c Release -r win-x64 --self-contained false --no-incremental
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "`n[3/3] Publishing single file..." -ForegroundColor Yellow
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:SelfContained=false -p:PublishSelfContained=false -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -p:DebugType=none -p:DebugSymbols=false --no-build
if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!" -ForegroundColor Red
    exit 1
}

$publishPath = "bin\Release\net10.0-windows\win-x64\publish"
$outputPath = Join-Path $projectPath $publishPath
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Build completed successfully!" -ForegroundColor Green
Write-Host "Output: $outputPath\系统工具箱.exe" -ForegroundColor White
Write-Host "========================================" -ForegroundColor Cyan

Get-ChildItem -Path $publishPath -Filter "*.exe" | ForEach-Object {
    $size = [math]::Round($_.Length / 1MB, 2)
    Write-Host "File: $($_.Name) - Size: $size MB" -ForegroundColor Gray
}
