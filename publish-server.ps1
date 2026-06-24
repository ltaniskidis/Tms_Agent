# PowerShell Script to Publish the TMS Central Server

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "TMS Central Server Publisher" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

$currentDir = Get-Location
$publishDir = Join-Path $currentDir "PublishAndSetup"
$serverPublishDir = Join-Path $publishDir "CentralServer"

Write-Host "Creating output folder for Central Server..." -ForegroundColor Yellow
if (!(Test-Path $serverPublishDir)) {
    New-Item -ItemType Directory -Path $serverPublishDir | Out-Null
}

Write-Host "Publishing Central Server..." -ForegroundColor Yellow
& "C:\Program Files\dotnet\dotnet.exe" publish Tms.CentralManagement\Tms.CentralManagement.csproj -c Release -o $serverPublishDir --self-contained false

# Create wwwroot/packages folder in published Central Server
$packagesFolder = Join-Path $serverPublishDir "wwwroot\packages"
if (!(Test-Path $packagesFolder)) {
    New-Item -ItemType Directory -Path $packagesFolder | Out-Null
}

Write-Host "=============================================" -ForegroundColor Green
Write-Host "Server publish completed successfully!" -ForegroundColor Green
Write-Host "Output folder: $serverPublishDir" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
