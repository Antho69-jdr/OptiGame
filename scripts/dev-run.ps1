# Compile puis relance OptiGame, en fermant proprement l'instance en cours si besoin.
#
# OptiGame tourne en administrateur : un terminal normal ne peut ni l'arrêter ni écraser ses DLL.
# Le script dépose donc une demande d'arrêt (quit.request) dans le dossier de données, qu'OptiGame surveille ;
# s'il y a une session de jeu en cours, ses réglages sont restaurés avant la fermeture.
#
# Usage (depuis la racine du dépôt) :  .\scripts\dev-run.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dataDir = if ($env:OPTIGAME_DATA_DIR) { $env:OPTIGAME_DATA_DIR } else { Join-Path $env:LOCALAPPDATA 'OptiGame' }
$exe = Join-Path $root 'src\OptiGame.App\bin\Debug\net10.0-windows\OptiGame.exe'

$running = Get-Process OptiGame -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "OptiGame est ouvert : demande de fermeture..."
    New-Item -ItemType Directory -Force $dataDir | Out-Null
    Set-Content -Path (Join-Path $dataDir 'quit.request') -Value (Get-Date -Format o) -Encoding UTF8

    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Process OptiGame -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 300
    }
    if (Get-Process OptiGame -ErrorAction SilentlyContinue) {
        Write-Host "OptiGame ne s'est pas fermé (version trop ancienne pour comprendre la demande ?)." -ForegroundColor Yellow
        Write-Host "Quittez-le une fois à la main : clic droit sur son icône > Quitter, puis relancez ce script." -ForegroundColor Yellow
        exit 1
    }
    Write-Host "OptiGame fermé."
}

dotnet build (Join-Path $root 'src\OptiGame.App')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Start-Process passe par ShellExecute : l'invite UAC s'affiche (dotnet run échoue avec l'erreur 740).
Start-Process $exe
