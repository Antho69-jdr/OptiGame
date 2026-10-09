# Déploie le serveur de mise en relation des appels (server/call-relay/worker.js) sur le compte Cloudflare de l'auteur
# d'OptiGame, par l'API de Cloudflare (aucun outil à installer : curl.exe de Windows). Plan gratuit : rien ne peut être facturé.
#   .\scripts\deploy-call-relay.ps1
# Clé d'accès : jeton d'API Cloudflare au modèle « Edit Cloudflare Workers », demandé à l'écran (jamais écrit sur le disque) ou
# lu dans $env:CLOUDFLARE_API_TOKEN. Affiche à la fin l'adresse wss://… à mettre dans Core/Call/CallRelay.DefaultUrl.
param([string]$Name = 'optigame-call')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$worker = Join-Path $repo 'server\call-relay\worker.js'
$api = 'https://api.cloudflare.com/client/v4'

$token = $env:CLOUDFLARE_API_TOKEN
if (-not $token) {
    $secure = Read-Host -AsSecureString 'Jeton d''API Cloudflare (modèle « Edit Cloudflare Workers »)'
    $token = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}
$headers = @{ Authorization = "Bearer $token" }

function Call-Api([string]$Method, [string]$Path, $Body) {
    $arguments = @{ Method = $Method; Uri = "$api$Path"; Headers = $headers }
    if ($null -ne $Body) { $arguments.Body = ($Body | ConvertTo-Json -Depth 6 -Compress); $arguments.ContentType = 'application/json' }
    $response = Invoke-RestMethod @arguments
    if (-not $response.success) { throw "Cloudflare refuse : $($response.errors | ConvertTo-Json -Compress)" }
    return $response.result
}

$accounts = @(Call-Api 'GET' '/accounts' $null)
if ($accounts.Count -ne 1) { throw "Le jeton donne accès à $($accounts.Count) comptes : limitez-le à un seul compte." }
$account = $accounts[0].id
Write-Host "Compte : $($accounts[0].name)"

$subdomain = (Call-Api 'GET' "/accounts/$account/workers/subdomain" $null).subdomain
if (-not $subdomain) { throw 'Aucun sous-domaine workers.dev : ouvrez une fois « Workers et Pages » dans le tableau de bord Cloudflare pour le choisir.' }

# Envoi du script (module) + objet durable « Room » (stockage SQLite, seul permis par le plan gratuit).
function Send-Worker([bool]$WithMigration) {
    $metadata = @{
        main_module        = 'worker.js'
        compatibility_date = '2026-09-01'
        bindings           = @(@{ type = 'durable_object_namespace'; name = 'ROOMS'; class_name = 'Room' })
    }
    if ($WithMigration) { $metadata.migrations = @{ new_tag = 'v1'; new_sqlite_classes = @('Room') } }
    $metadataFile = Join-Path $env:TEMP 'optigame-call-metadata.json'
    [IO.File]::WriteAllText($metadataFile, ($metadata | ConvertTo-Json -Depth 6 -Compress), (New-Object Text.UTF8Encoding $false))
    try {
        $output = & curl.exe -sS -X PUT "$api/accounts/$account/workers/scripts/$Name" `
            -H "Authorization: Bearer $token" `
            -F "metadata=@$metadataFile;type=application/json" `
            -F "worker.js=@$worker;filename=worker.js;type=application/javascript+module"
    }
    finally { Remove-Item $metadataFile -ErrorAction SilentlyContinue }
    return ($output | ConvertFrom-Json)
}
$result = Send-Worker $true
if (-not $result.success -and ($result.errors | ConvertTo-Json -Compress) -match 'migration') { $result = Send-Worker $false }
if (-not $result.success) { throw "Envoi refusé : $($result.errors | ConvertTo-Json -Compress)" }

Call-Api 'POST' "/accounts/$account/workers/scripts/$Name/subdomain" @{ enabled = $true; previews_enabled = $false } | Out-Null
$url = "wss://$Name.$subdomain.workers.dev"
Write-Host "Serveur déployé : $url"
Write-Host "À mettre dans src\OptiGame.Core\Call\CallRelay.cs (DefaultUrl), ou pour un essai : `$env:OPTIGAME_CALL_RELAY='$url'"
