# Fabrique l'installeur d'OptiGame : artifacts\installer\OptiGame-Setup-<version>.exe
#
# 1. Publication AUTONOME (.NET inclus : rien à installer sur la machine cible), compilée d'avance (démarrage plus rapide).
# 2. PresentMon (mesure des FPS) ajouté dans tools\ : version épinglée, empreinte SHA-256 et signature Intel vérifiées.
# 3. Inno Setup compile installer\OptiGame.iss.
#
# Prérequis : SDK .NET 10 et Inno Setup 6.7 ou plus récent (winget install --id JRSoftware.InnoSetup -e).
# Usage (depuis la racine du dépôt) : .\scripts\build-installer.ps1      (version lue dans Directory.Build.props)
# -Step Publish : étapes 1 et 2 seulement ; -Step Package : étape 3 seulement, sur artifacts\publish déjà prêt. La CI signe
# (SignPath) les fichiers d'OptiGame entre les deux, puis l'installeur.

param(
    [string]$Version,
    [ValidateSet('All', 'Publish', 'Package')][string]$Step = 'All'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # téléchargements bien plus rapides sans barre de progression
$root = Split-Path -Parent $PSScriptRoot

if (-not $Version) {
    $props = [xml](Get-Content -Raw (Join-Path $root 'Directory.Build.props'))
    $Version = @($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
}
if (-not $Version) { throw 'Version introuvable dans Directory.Build.props.' }

$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
$installer = Join-Path $artifacts 'installer'
$cache = Join-Path $artifacts 'cache'

if ($Step -ne 'Package') {
    # 1. Publication autonome.
    Write-Host "OptiGame $Version : publication autonome (win-x64)..." -ForegroundColor Cyan
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    dotnet publish (Join-Path $root 'src\OptiGame.App\OptiGame.App.csproj') -c Release -r win-x64 --self-contained true `
        -p:PublishReadyToRun=true -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" -o $publish
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    # 2. PresentMon : seule cette version, à cette empreinte, signée par Intel, entre dans l'installeur.
    $pmVersion = '2.6.0'
    $pmSha256 = 'B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF'
    $pmName = "PresentMon-$pmVersion-x64.exe"
    New-Item -ItemType Directory -Force $cache | Out-Null
    $pmCached = Join-Path $cache $pmName
    if (-not (Test-Path $pmCached) -or (Get-FileHash $pmCached -Algorithm SHA256).Hash -ne $pmSha256) {
        Write-Host "Téléchargement de $pmName (github.com/GameTechDev/PresentMon)..." -ForegroundColor Cyan
        Invoke-WebRequest "https://github.com/GameTechDev/PresentMon/releases/download/v$pmVersion/$pmName" -OutFile $pmCached -UseBasicParsing
    }
    $hash = (Get-FileHash $pmCached -Algorithm SHA256).Hash
    if ($hash -ne $pmSha256) {
        Remove-Item $pmCached -Force
        throw "PresentMon : empreinte inattendue ($hash), fichier refusé."
    }
    $signature = Get-AuthenticodeSignature $pmCached
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Intel Corporation') {
        throw "PresentMon : signature Intel non valide ($($signature.Status)), fichier refusé."
    }
    $tools = Join-Path $publish 'tools'
    New-Item -ItemType Directory -Force $tools | Out-Null
    Copy-Item $pmCached (Join-Path $tools $pmName)
}
if ($Step -eq 'Publish') {
    Write-Host "Publication prête : $publish" -ForegroundColor Green
    exit 0
}
if (-not (Test-Path (Join-Path $publish 'OptiGame.exe'))) { throw "Publication absente ($publish) : lancez d'abord -Step Publish." }

# 3. Inno Setup.
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
if (-not $iscc) {
    Write-Host 'Inno Setup 6 est introuvable. Installez-le, puis relancez ce script :' -ForegroundColor Yellow
    Write-Host '  winget install --id JRSoftware.InnoSetup -e' -ForegroundColor Yellow
    exit 1
}
Write-Host "Compilation de l'installeur (Inno Setup)..." -ForegroundColor Cyan
& $iscc /Qp "/DAppVersion=$Version" "/DPublishDir=$publish" "/O$installer" (Join-Path $root 'installer\OptiGame.iss')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$setup = Join-Path $installer "OptiGame-Setup-$Version.exe"
Write-Host ("Installeur prêt : {0} ({1:N0} Mo)" -f $setup, ((Get-Item $setup).Length / 1MB)) -ForegroundColor Green
