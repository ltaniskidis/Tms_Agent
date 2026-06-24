# PowerShell Script to Publish the TMS Agent Setup Package

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "TMS Agent Setup Publisher" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

$currentDir = Get-Location
$publishDir = Join-Path $currentDir "PublishAndSetup"
$agentSetupDir = Join-Path $publishDir "AgentSetup"

Write-Host "Creating output folder for Agent Setup..." -ForegroundColor Yellow
if (!(Test-Path $agentSetupDir)) {
    New-Item -ItemType Directory -Path $agentSetupDir | Out-Null
}

$agentDir = Join-Path $publishDir "Agent"
if (!(Test-Path $agentDir)) {
    New-Item -ItemType Directory -Path $agentDir | Out-Null
}

Write-Host "Publishing WPF Agent (win-x64, Self-Contained) to AgentSetup..." -ForegroundColor Yellow
& "C:\Program Files\dotnet\dotnet.exe" publish Tms.Agent.Wpf\Tms.Agent.Wpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $agentSetupDir

Write-Host "Publishing WPF Agent (win-x64, Self-Contained) to Agent..." -ForegroundColor Yellow
& "C:\Program Files\dotnet\dotnet.exe" publish Tms.Agent.Wpf\Tms.Agent.Wpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $agentDir

# Write ReadMe file
$readmeContent = @"
======================================================================
TMS AGENT - ΟΔΗΓΟΣ ΕΓΚΑΤΑΣΤΑΣΗΣ (SETUP GUIDE)
======================================================================

Αυτός ο φάκελος περιέχει το εκτελέσιμο αρχείο για την εγκατάσταση του Agent:

1. ΕΚΤΕΛΕΣΙΜΟ: Tms.Agent.Wpf.exe
   ---------------------------------
   - Το αρχείο είναι FULLY SELF-CONTAINED (αυτόνομο). Δεν χρειάζεται καμία εγκατάσταση .NET SDK ή Runtime στον υπολογιστή του πελάτη.
   - Απλά αντιγράψτε το 'Tms.Agent.Wpf.exe' στο μηχάνημα του πελάτη και εκτελέστε το με διπλό κλικ.
   - Ο Agent θα ελαχιστοποιηθεί αυτόματα στο System Tray (κάτω δεξιά).

2. ΑΥΤΟΜΑΤΗ ΕΚΚΙΝΗΣΗ & ΠΑΡΑΚΑΜΨΗ UAC
   ---------------------------------
   - Κατά την πρώτη εκκίνηση, ο Agent εγγράφεται αυτόματα στο Task Scheduler των Windows (Χρονοδιάγραμμα Εργασιών) με όνομα "TmsAgent".
   - Αυτό επιτρέπει στον Agent να εκκινείται αυτόματα κατά τη σύνδεση του χρήστη (on logon) με τα υψηλότερα δικαιώματα (Highest privileges), παρακάμπτοντας τα ενοχλητικά μηνύματα UAC των Windows.
   - Υποστηρίζει την παράμετρο '--startup' για σιωπηλή εκκίνηση απευθείας στο System Tray.

======================================================================
"@

$readmePath = Join-Path $agentSetupDir "ReadMe_AgentSetup.txt"
Set-Content -Path $readmePath -Value $readmeContent -Encoding utf8

Write-Host "=============================================" -ForegroundColor Green
Write-Host "Agent setup publish completed successfully!" -ForegroundColor Green
Write-Host "Output folder: $agentSetupDir" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
