param([string]$BaseUrl = 'http://localhost:8080')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$settings = @{}
Get-Content -LiteralPath (Join-Path $projectRoot '.env') | ForEach-Object {
    if ($_ -match '^([^#=]+)=(.*)$') { $settings[$matches[1]] = $matches[2] }
}
function New-Session { return New-Object Microsoft.PowerShell.Commands.WebRequestSession }
function Submit-View($session, $getPath, $postPath, $fields) {
    $page = Invoke-WebRequest -Uri "$BaseUrl$getPath" -WebSession $session -UseBasicParsing -TimeoutSec 30
    $match = [regex]::Match($page.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')
    if (-not $match.Success) { throw "Missing antiforgery field in $getPath" }
    $fields['__RequestVerificationToken'] = [System.Net.WebUtility]::HtmlDecode($match.Groups[1].Value)
    $result = Invoke-WebRequest -Uri "$BaseUrl$postPath" -Method POST -WebSession $session -UseBasicParsing -Body $fields -TimeoutSec 30
    if ($result.StatusCode -ne 200 -or $result.Content -match "We couldn&#x27;t complete|We couldn't complete") { throw "Razor form failed: $postPath" }
    Write-Output "PASS: Razor form $postPath" | Out-Host
    return $result.Content
}
function Request($session, $method, $path, $body = $null, $expected = 200) {
    $parameters = @{ Uri = "$BaseUrl$path"; Method = $method; WebSession = $session; UseBasicParsing = $true; TimeoutSec = 30 }
    if ($method -ne 'GET') {
        $csrf = Invoke-RestMethod -Uri "$BaseUrl/api/auth/csrf" -WebSession $session -TimeoutSec 30
        $parameters.Headers = @{ 'X-CSRF-TOKEN' = $csrf.token }
        $parameters.ContentType = 'application/json'
        if ($null -ne $body) { $parameters.Body = ($body | ConvertTo-Json -Depth 8) }
    }
    try { $response = Invoke-WebRequest @parameters; $status = [int]$response.StatusCode }
    catch { if ($null -eq $_.Exception.Response) { throw }; $status = [int]$_.Exception.Response.StatusCode; $response = $null }
    if ($status -ne $expected) { throw "$method $path returned $status; expected $expected" }
    Write-Output "PASS: $method $path ($status)" | Out-Host
    if ($response -and $response.Content) { return $response.Content | ConvertFrom-Json }
}
$anonymous = New-Session
Request $anonymous GET '/api/dashboard' $null 401 | Out-Null
$admin = New-Session
Request $admin POST '/api/auth/login' @{ email=$settings.SEED_ADMIN_EMAIL; password=$settings.SEED_ADMIN_PASSWORD } | Out-Null
$viewAdmin = New-Session
$html = Submit-View $viewAdmin '/Workspace/Login' '/Workspace/Login' @{ email=$settings.SEED_ADMIN_EMAIL; password=$settings.SEED_ADMIN_PASSWORD }
if ($html -notmatch 'Operations overview' -or $html -match '<script') { throw 'Razor login did not reach the JavaScript-free dashboard.' }
$dashboard = Request $admin GET '/api/dashboard'
if ($dashboard.dataMode -ne 'Demo') { throw 'Smoke test requires Demo mode so it does not change real operational tasks.' }
for ($attempt = 0; $attempt -lt 30 -and @($dashboard.stations).Count -lt 12; $attempt++) {
    Start-Sleep -Seconds 4
    $dashboard = Request $admin GET '/api/dashboard'
}
if (@($dashboard.stations).Count -lt 12) { throw 'Demo telemetry did not arrive through Kafka within two minutes.' }
$stationId = $dashboard.stations[0].station.id
Request $admin GET "/api/stations/$stationId" | Out-Null
$history = Request $admin GET "/api/stations/$stationId/history"
if (@($history).Count -lt 2) { throw 'Station history is missing.' }
Request $admin GET '/api/stations/not-a-real-station' $null 404 | Out-Null
Request $admin GET '/api/alerts' | Out-Null
$email = 'smoke-' + [Guid]::NewGuid().ToString('N') + '@example.invalid'
$operatorPassword = [Guid]::NewGuid().ToString('N') + '!'
$user = Request $admin POST '/api/users' @{ name='Smoke Test Operator'; email=$email; password=$operatorPassword; role='OPERATOR'; enabled=$true } 201
try {
    if ($user.PSObject.Properties.Name -contains 'passwordHash') { throw 'API exposed a password hash.' }
    Submit-View $viewAdmin "/Workspace/EditUser/$($user.id)" '/Workspace/SaveUser' @{
        id=$user.id; name='Smoke Test Operator Updated'; email=$email; role='OPERATOR'; enabled='true'; password='' } | Out-Null
    $edited = Request $admin GET "/api/users/$($user.id)"
    if ($edited.name -ne 'Smoke Test Operator Updated') { throw 'Editing the user through Razor did not persist.' }
    $operator = New-Session
    Request $operator POST '/api/auth/login' @{ email=$email; password=$operatorPassword } | Out-Null
    Request $operator GET '/api/users' $null 403 | Out-Null
    Request $operator GET '/api/dashboard' | Out-Null
    $moves = @(Request $operator GET '/api/recommendations?status=PENDING')
    for ($attempt = 0; $attempt -lt 15 -and $moves.Count -eq 0; $attempt++) {
        Start-Sleep -Seconds 4; $moves = @(Request $operator GET '/api/recommendations?status=PENDING')
    }
    if ($moves.Count -eq 0) { throw 'No recommendation was generated.' }
    $moveId = $moves[0].id
    $viewOperator = New-Session
    $operatorHtml = Submit-View $viewOperator '/Workspace/Login' '/Workspace/Login' @{ email=$email; password=$operatorPassword }
    if ($operatorHtml -match 'Team management') { throw 'Operator navigation exposed user administration.' }
    Submit-View $viewOperator "/Workspace/Confirm/$moveId`?kind=recommendation&status=COMPLETED" '/Workspace/Apply' @{
        kind='recommendation'; id=$moveId; status='COMPLETED' } | Out-Null
    $saved = Request $operator GET "/api/recommendations/$moveId"
    if ($saved.status -ne 'COMPLETED') { throw 'Completion was not persisted.' }
    Request $operator POST '/api/auth/logout' $null 204 | Out-Null
    Request $operator POST '/api/auth/login' @{ email=$email; password=$operatorPassword } | Out-Null
    $reloaded = Request $operator GET "/api/recommendations/$moveId"
    if ($reloaded.status -ne 'COMPLETED') { throw 'Completion did not survive a new session.' }
    Request $admin PATCH "/api/users/$($user.id)/enabled" @{ enabled=$false } | Out-Null
    Request $operator GET '/api/dashboard' $null 401 | Out-Null
    Write-Output 'End-to-end smoke test passed: Kafka readings, history, admin/operator authorization and persisted task completion.'
}
finally {
    Request $admin PATCH "/api/users/$($user.id)/enabled" @{ enabled=$false } | Out-Null
}
