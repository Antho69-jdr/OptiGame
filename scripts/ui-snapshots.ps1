# Captures de chaque page d'OptiGame en PNG (développement de l'interface) : compare l'avant / l'après d'une modification.
# Données : copie des vraies (profils, temps de jeu, mesures, jaquettes, réglages) dans %TEMP%\OptiGame-ui\data, jamais
# session.json ; l'appli tourne avec OPTIGAME_DATA_DIR, sans élévation (RunAsInvoker), hors des écrans, sans rien démarrer
# (ni détection, ni dock, ni mise à jour). Lecture seule : rien n'est appliqué.
#   .\scripts\ui-snapshots.ps1 -Label avant
#   .\scripts\ui-snapshots.ps1 -Label apres -Sizes 880x600,1240x860 -Game "Overwatch"
#   .\scripts\ui-snapshots.ps1 -Label dialogues -Only 7,0      (préfixes : 0 galerie, 1-6 pages, 7 dialogues)
# Résultat : artifacts\ui-snapshots\<Label>\<page>-<largeur>x<hauteur>.png + snapshot.log
param(
    [string]$Label = 'courant',
    [string[]]$Sizes = @(),
    [string]$Game = '',
    [string[]]$Only = @(),
    [switch]$Reseed
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$work = Join-Path $env:TEMP 'OptiGame-ui'
$data = Join-Path $work 'data'
$build = Join-Path $work 'build'
$real = Join-Path $env:LOCALAPPDATA 'OptiGame'

if ($Reseed -and (Test-Path $data)) { Remove-Item -Recurse -Force $data }
if (-not (Test-Path $data)) {
    New-Item -ItemType Directory -Force $data | Out-Null
    foreach ($file in 'profiles.json', 'playtime.json', 'requirements.json', 'settings.json', 'fixes.json') {
        $source = Join-Path $real $file
        if (Test-Path $source) { Copy-Item $source $data }
    }
    foreach ($dir in 'captures', 'covers') {
        $source = Join-Path $real $dir
        if (Test-Path $source) { Copy-Item -Recurse $source (Join-Path $data $dir) }
    }
    Write-Host "Données copiées dans $data"
}

dotnet build (Join-Path $repo 'src\OptiGame.App\OptiGame.App.csproj') -c Debug -o $build -v quiet -nologo
if ($LASTEXITCODE -ne 0) { throw 'Compilation échouée.' }

$output = Join-Path $repo "artifacts\ui-snapshots\$Label"
if (Test-Path $output) { Remove-Item -Recurse -Force $output }
$arguments = @('--snapshot', "`"$output`"")
if ($Sizes) { $arguments += @('--sizes', ($Sizes -join ',')) }
if ($Game) { $arguments += @('--game', "`"$Game`"") }
if ($Only) { $arguments += @('--only', ($Only -join ',')) }

$env:__COMPAT_LAYER = 'RunAsInvoker'
$env:OPTIGAME_DATA_DIR = $data
try {
    $process = Start-Process -FilePath (Join-Path $build 'OptiGame.exe') -ArgumentList $arguments -PassThru -Wait
}
finally {
    Remove-Item Env:\__COMPAT_LAYER, Env:\OPTIGAME_DATA_DIR -ErrorAction SilentlyContinue
}
Get-Content -Encoding UTF8 (Join-Path $output 'snapshot.log')
if ($process.ExitCode -ne 0) { throw "OptiGame s'est arrêté avec le code $($process.ExitCode)." }
