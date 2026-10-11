$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$envPath = Join-Path $projectRoot '.env'
if (Test-Path -LiteralPath $envPath) { Write-Output 'Existing .env kept unchanged.'; exit 0 }
function New-LocalPassword {
    $bytes = New-Object byte[] 24
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $random.GetBytes($bytes)
    $random.Dispose()
    return [Convert]::ToBase64String($bytes)
}
$databasePassword = New-LocalPassword
$administratorPassword = New-LocalPassword
$lines = @("MYSQL_ROOT_PASSWORD=$databasePassword", 'SEED_ADMIN_EMAIL=admin@bike.local',
    "SEED_ADMIN_PASSWORD=$administratorPassword",
    'GBFS_BASE_URL=https://gbfs.lyft.com/gbfs/2.3/bkn/en/', 'GBFS_STATION_PREFIX=citibike:', 'UI_REFRESH_SECONDS=60', 'APP_PORT=8080')
[System.IO.File]::WriteAllLines($envPath, $lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Output 'Created .env with randomly generated local credentials. Open it locally to read the admin login. Do not commit it.'
