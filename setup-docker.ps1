# Script to setup host directories and copy existing SQLite database and update packages

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "TMS Docker Environment Setup" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

$dataDir = "./data"
$packagesDir = "./packages"

# Create host directories if they don't exist
if (!(Test-Path $dataDir)) {
    Write-Host "Creating host data directory: $dataDir..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $dataDir | Out-Null
}

if (!(Test-Path $packagesDir)) {
    Write-Host "Creating host packages directory: $packagesDir..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $packagesDir | Out-Null
}

# Copy database files if they exist locally and do not already exist in destination
if (Test-Path "Tms.CentralManagement/central.db") {
    if (!(Test-Path "$dataDir/central.db")) {
        Write-Host "Copying existing SQLite database files to $dataDir..." -ForegroundColor Yellow
        Copy-Item -Path "Tms.CentralManagement/central.db" -Destination "$dataDir/central.db" -Force
    } else {
        Write-Host "Active database already exists in $dataDir. Skipping copy to preserve runtime changes." -ForegroundColor Yellow
    }
} else {
    Write-Host "No existing database found in Tms.CentralManagement/central.db. A fresh one will be created on start." -ForegroundColor Gray
}

# Copy existing packages if they exist locally
if (Test-Path "Tms.CentralManagement/wwwroot/packages") {
    $existingZips = Get-ChildItem -Path "Tms.CentralManagement/wwwroot/packages" -Filter "*.zip"
    if ($existingZips.Count -gt 0) {
        Write-Host "Copying $($existingZips.Count) existing package ZIP files from wwwroot/packages to $packagesDir..." -ForegroundColor Yellow
        Copy-Item -Path "Tms.CentralManagement/wwwroot/packages/*.zip" -Destination "$packagesDir/" -Force
    }
}

if (Test-Path "PublishAndSetup") {
    $publishZips = Get-ChildItem -Path "PublishAndSetup" -Filter "*.zip"
    if ($publishZips.Count -gt 0) {
        Write-Host "Copying $($publishZips.Count) package ZIP files from PublishAndSetup to $packagesDir..." -ForegroundColor Yellow
        Copy-Item -Path "PublishAndSetup/*.zip" -Destination "$packagesDir/" -Force
    }
}


Write-Host "=============================================" -ForegroundColor Green
Write-Host "Setup completed successfully!" -ForegroundColor Green
Write-Host "You can now run: docker compose up --build -d" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
