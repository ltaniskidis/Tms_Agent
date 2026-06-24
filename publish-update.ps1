# PowerShell Script to Package and Publish a new Update Version for TMS

param (
    [string]$Version
)

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "TMS Version Update Publisher" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# 1. Prompt for Version if not provided
if ([string]::IsNullOrEmpty($Version)) {
    $Version = Read-Host "Enter version number (e.g. 1.2.0)"
}

$Version = $Version.Trim()

# Validate 3-level version structure (e.g. 1.2.3)
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Error "Error: Version must be in 3-level format (e.g. 1.2.0, 1.1.1)."
    exit 1
}

# 2. Prompt for Scope (Server, Client, Server & Client)
Write-Host "Which components does this update affect?" -ForegroundColor Yellow
Write-Host "1) Server"
Write-Host "2) Client"
Write-Host "3) Server & Client (Both)"
$choice = Read-Host "Selection (1-3)"

switch ($choice) {
    "1" { $scope = "Server" }
    "2" { $scope = "Client" }
    "3" { $scope = "Server & Client" }
    default {
        Write-Warning "Invalid selection. Defaulting to: Server & Client"
        $scope = "Server & Client"
    }
}

Write-Host "`nPackaging version: $Version (Scope: $scope)..." -ForegroundColor Cyan

# 3. Paths Setup
$currentDir = Get-Location
$tempDir = Join-Path $currentDir "PublishAndSetup\TempUpdate"
$zipName = "app_$Version.zip"

# Destination directories
$serverPackagesDev = Join-Path $currentDir "Tms.CentralManagement\wwwroot\packages"
$serverPackagesProd = Join-Path $currentDir "PublishAndSetup\CentralServer\wwwroot\packages"

# Ensure directories exist
if (Test-Path $tempDir) {
    Remove-Item -Recurse -Force $tempDir
}
New-Item -ItemType Directory -Path $tempDir | Out-Null

if (!(Test-Path $serverPackagesDev)) {
    New-Item -ItemType Directory -Path $serverPackagesDev | Out-Null
}

# 4. Compile and Publish updated Client/Agent binaries to temp directory
Write-Host "Publishing WPF Agent binaries..." -ForegroundColor Yellow
& "C:\Program Files\dotnet\dotnet.exe" publish Tms.Agent.Wpf\Tms.Agent.Wpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $tempDir

# 5. Compress to ZIP
$zipPath = Join-Path $currentDir "PublishAndSetup\$zipName"
if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

Write-Host "Creating ZIP archive: $zipName..." -ForegroundColor Yellow
Compress-Archive -Path "$tempDir\*" -DestinationPath $zipPath -Force

# 6. Serving from PublishAndSetup directly
Write-Host "Update package is stored directly in PublishAndSetup." -ForegroundColor Cyan

# 7. Cleanup
if (Test-Path $tempDir) {
    Remove-Item -Recurse -Force $tempDir
}
# Keep the packaged update ZIP in the PublishAndSetup folder
Write-Host "Keeping update package in: PublishAndSetup\$zipName" -ForegroundColor Green

Write-Host "`n=============================================" -ForegroundColor Green
Write-Host "Update package published successfully!" -ForegroundColor Green
Write-Host "Version: $Version" -ForegroundColor Green
Write-Host "Scope: $scope" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
Write-Host "`nREGISTRATION INSTRUCTIONS:" -ForegroundColor Yellow
Write-Host "1. Open the Web Dashboard (e.g. http://localhost:5246/Versions)."
Write-Host "2. Add a new version with version number: $Version"
Write-Host "3. Set the binary package URL to: /packages/$zipName"
Write-Host "4. Add the description: (Scope: $scope) - [Describe your changes]"
Write-Host "=============================================" -ForegroundColor Green
