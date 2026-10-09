# Déploie le serveur de mise en relation des appels (server/call-relay/worker.js) sur le compte Cloudflare de l'auteur
# d'OptiGame, par l'API de Cloudflare (aucun outil à installer : curl.exe de Windows). Plan gratuit : rien ne peut être facturé.
#   .\scripts\deploy-call-relay.ps1
# Clé d'accès : jeton d'API Cloudflare au modèle « Edit Cloudflare Workers », demandé à l'écran (jamais écrit sur le disque) ou
# lu dans $env:CLOUDFLARE_API_TOKEN ; jeton de compte : -AccountId <ID>. Clé d'API Web de Steam (amis) : demandée la 1re fois, ou
# $env:STEAM_API_KEY pour la changer. Affiche à la fin l'adresse wss://… à mettre dans Core/Call/CallRelay.DefaultUrl.
param([string]$Name = 'optigame-call', [string]$AccountId = $env:CLOUDFLARE_ACCOUNT_ID)
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

# Jeton de compte (« cfat_… », Gérer le compte › Jetons d'API du compte) : il ne peut pas lister les comptes, l'ID est donné
# (-AccountId, ou CLOUDFLARE_ACCOUNT_ID ; il figure dans l'adresse du tableau de bord). Jeton d'utilisateur : compte trouvé seul.
if ($AccountId) {
    $account = $AccountId
    $verify = Call-Api 'GET' "/accounts/$account/tokens/verify" $null
    # Un jeton à date de début future est « actif » pour la vérification, mais refusé partout ailleurs (constaté le 2026-10-09).
    if ($verify.not_before -and ([datetime]$verify.not_before).ToUniversalTime() -gt (Get-Date).ToUniversalTime()) {
        throw "Ce jeton n'est utilisable qu'à partir du $(([datetime]$verify.not_before).ToLocalTime()) : retirez sa date de début, ou attendez."
    }
}
else {
    $accounts = @(Call-Api 'GET' '/accounts' $null)
    if ($accounts.Count -ne 1) { throw "Le jeton donne accès à $($accounts.Count) comptes : limitez-le à un seul compte, ou donnez -AccountId." }
    $account = $accounts[0].id
}
Write-Host "Compte : $account"

$subdomain = (Call-Api 'GET' "/accounts/$account/workers/subdomain" $null).subdomain
if (-not $subdomain) { throw 'Aucun sous-domaine workers.dev : ouvrez une fois « Workers et Pages » dans le tableau de bord Cloudflare pour le choisir.' }

# Envoi du script (module) + objets durables (stockage SQLite, seul permis par le plan gratuit) : Room (salons, migration v1),
# Login et Presence (connexion avec Steam, amis en ligne, v2). Les secrets déjà posés sont gardés (keep_bindings).
$migrations = @(
    @{ old_tag = 'v1'; new_tag = 'v2'; new_sqlite_classes = @('Login', 'Presence') },                    # déjà en v1
    @{ new_tag = 'v2'; steps = @(@{ new_sqlite_classes = @('Room') }, @{ new_sqlite_classes = @('Login', 'Presence') }) }, # compte neuf
    $null                                                                                                  # déjà en v2
)
function Send-Worker($Migration) {
    $metadata = @{
        main_module        = 'worker.js'
        compatibility_date = '2026-09-01'
        bindings           = @(
            @{ type = 'durable_object_namespace'; name = 'ROOMS'; class_name = 'Room' },
            @{ type = 'durable_object_namespace'; name = 'LOGINS'; class_name = 'Login' },
            @{ type = 'durable_object_namespace'; name = 'PRESENCE'; class_name = 'Presence' }
        )
        keep_bindings      = @('secret_text')
    }
    if ($Migration) { $metadata.migrations = $Migration }
    $metadataFile = Join-Path $env:TEMP 'optigame-call-metadata.json'
    [IO.File]::WriteAllText($metadataFile, ($metadata | ConvertTo-Json -Depth 8 -Compress), (New-Object Text.UTF8Encoding $false))
    try {
        $output = & curl.exe -sS -X PUT "$api/accounts/$account/workers/scripts/$Name" `
            -H "Authorization: Bearer $token" `
            -F "metadata=@$metadataFile;type=application/json" `
            -F "worker.js=@$worker;filename=worker.js;type=application/javascript+module"
    }
    finally { Remove-Item $metadataFile -ErrorAction SilentlyContinue }
    return ($output | ConvertFrom-Json)
}
foreach ($migration in $migrations) {
    $result = Send-Worker $migration
    if ($result.success -or ($result.errors | ConvertTo-Json -Compress) -notmatch 'migration|tag') { break }
}
if (-not $result.success) { throw "Envoi refusé : $($result.errors | ConvertTo-Json -Compress)" }

# Secrets : signature des jetons (tiré au hasard une seule fois : le changer déconnecte tout le monde) et clé d'API Web de Steam.
function Set-Secret([string]$SecretName, [string]$Value) {
    Call-Api 'PUT' "/accounts/$account/workers/scripts/$Name/secrets" @{ name = $SecretName; text = $Value; type = 'secret_text' } | Out-Null
    Write-Host "Secret $SecretName posé."
}
$secrets = @(Call-Api 'GET' "/accounts/$account/workers/scripts/$Name/secrets" $null | ForEach-Object { $_.name })
if ($secrets -notcontains 'TOKEN_SECRET') {
    $bytes = New-Object byte[] 32
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    Set-Secret 'TOKEN_SECRET' ([Convert]::ToBase64String($bytes))
}
$steamKey = $env:STEAM_API_KEY
if (-not $steamKey -and $secrets -notcontains 'STEAM_API_KEY') {
    $secure = Read-Host -AsSecureString 'Clé d''API Web de Steam (steamcommunity.com/dev/apikey)'
    $steamKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}
if ($steamKey) {
    if ($steamKey -notmatch '^[0-9A-F]{32}$') { throw 'Une clé d''API Web de Steam fait 32 caractères hexadécimaux.' }
    Set-Secret 'STEAM_API_KEY' $steamKey
}

Call-Api 'POST' "/accounts/$account/workers/scripts/$Name/subdomain" @{ enabled = $true; previews_enabled = $false } | Out-Null
$url = "wss://$Name.$subdomain.workers.dev"
Write-Host "Serveur déployé : $url"
Write-Host "À mettre dans src\OptiGame.Core\Call\CallRelay.cs (DefaultUrl), ou pour un essai : `$env:OPTIGAME_CALL_RELAY='$url'"
