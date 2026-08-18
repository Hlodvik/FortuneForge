[CmdletBinding()]
param(
    [string]$ProjectId = 'demo-fortuneforge-e2e',
    [string]$EmulatorHost = '127.0.0.1:8787',
    [string]$UserId = 'e2e-player',
    [string]$SessionToken = 'fortuneforge-e2e-session-token-000000000001',
    [string]$Email = 'e2e@fortuneforge.invalid',
    [string]$Password = 'FortuneForgeE2E!'
)

$ErrorActionPreference = 'Stop'
$baseUri = "http://$EmulatorHost/v1/projects/$ProjectId/databases/(default)/documents"
$now = [DateTime]::UtcNow.ToString('o')
$expires = [DateTime]::UtcNow.AddHours(2).ToString('o')

function String-Value([string]$value) { @{ stringValue = $value } }
function Integer-Value([long]$value) { @{ integerValue = $value.ToString([Globalization.CultureInfo]::InvariantCulture) } }
function Boolean-Value([bool]$value) { @{ booleanValue = $value } }
function Timestamp-Value([string]$value) { @{ timestampValue = $value } }

function Set-Document {
    param([string]$Path, [hashtable]$Fields)

    $body = @{ fields = $Fields } | ConvertTo-Json -Depth 12 -Compress
    Invoke-RestMethod -Method Patch -Uri "$baseUri/$Path" -ContentType 'application/json' -Body $body | Out-Null
}

$tokenBytes = [Text.Encoding]::UTF8.GetBytes($SessionToken)
$tokenHash = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($tokenBytes))
$emailBytes = [Text.Encoding]::UTF8.GetBytes($Email.ToLowerInvariant())
$emailHash = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($emailBytes))
$salt = [Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
$passwordHashBytes = [Security.Cryptography.Rfc2898DeriveBytes]::Pbkdf2(
    $Password,
    $salt,
    210000,
    [Security.Cryptography.HashAlgorithmName]::SHA256,
    32)
$passwordHash = "pbkdf2-sha256`$210000`$$([Convert]::ToBase64String($salt))`$$([Convert]::ToBase64String($passwordHashBytes))"

Set-Document "users/$UserId" @{
    userId = String-Value $UserId
    playerName = String-Value 'E2E Player'
    normalizedPlayerName = String-Value 'E2E PLAYER'
    email = String-Value $Email
    passwordHash = String-Value $passwordHash
    status = String-Value 'active'
    deactivated = Boolean-Value $false
    authProvider = String-Value 'e2e-local'
    firebaseUid = String-Value $UserId
    emailVerified = Boolean-Value $true
    role = String-Value 'player'
    accountSchemaVersion = Integer-Value 7
    createdAt = Timestamp-Value $now
    updatedAt = Timestamp-Value $now
}

Set-Document "accountEmailKeys/$emailHash" @{
    userId = String-Value $UserId
    createdAt = Timestamp-Value $now
}

foreach ($currency in @('slotsCredits', 'freeGames', 'specialPoints', 'energy')) {
    $available = if ($currency -eq 'slotsCredits') { 500L } else { 0L }
    Set-Document "userBalances/${UserId}_$currency" @{
        userId = String-Value $UserId
        currencyId = String-Value $currency
        available = Integer-Value $available
        availableFractionalCents = Integer-Value 0
        reserved = Integer-Value 0
        version = Integer-Value 1
        createdAt = Timestamp-Value $now
        updatedAt = Timestamp-Value $now
    }
}

Set-Document "userSlotStatistics/$UserId" @{
    userId = String-Value $UserId
    spinsPlayed = Integer-Value 0
    wins = Integer-Value 0
    losses = Integer-Value 0
    creditsWagered = Integer-Value 0
    creditsWon = Integer-Value 0
    netCredits = Integer-Value 0
    createdAt = Timestamp-Value $now
    updatedAt = Timestamp-Value $now
}

Set-Document "accountSessions/$tokenHash" @{
    userId = String-Value $UserId
    createdAt = Timestamp-Value $now
    expiresAt = Timestamp-Value $expires
    lastSeenAt = Timestamp-Value $now
    revoked = Boolean-Value $false
}

Write-Output "Seeded $Email for localhost end-to-end testing."
