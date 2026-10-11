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
if (@($dashboard.stations).Count -eq 0) { throw 'No source stations yet. Wait for ingestion, then rerun.' }
$stationId = $dashboard.stations[0].station.id
Request $admin GET "/api/stations/$stationId" | Out-Null
Request $admin GET "/api/stations/$stationId/history" | Out-Null
Request $admin GET '/api/metrics' | Out-Null
$riderEmail = 'test-rider-' + [Guid]::NewGuid().ToString('N') + '@example.invalid'
$riderPassword = [Guid]::NewGuid().ToString('N') + '!'
$riderSession = New-Session
Request $riderSession POST '/api/auth/register' @{ name='Integration Test Rider'; email=$riderEmail; password=$riderPassword; role='ADMIN' } | Out-Null
Request $riderSession POST '/api/auth/register' @{ name='Duplicate'; email=$riderEmail; password=$riderPassword } 409 | Out-Null
Request $riderSession POST '/api/auth/login' @{ email=$riderEmail; password='incorrect-password' } 401 | Out-Null
$rider = Request $riderSession POST '/api/auth/login' @{ email=$riderEmail; password=$riderPassword }
if ($rider.role -ne 'RIDER' -or $rider.PSObject.Properties.Name -contains 'passwordHash') { throw 'Unsafe registration response or role escalation.' }
$issues = @()
try {
    Request $riderSession GET '/api/dashboard' $null 403 | Out-Null
    Request $riderSession POST "/api/rider/favorites/$stationId" $null 204 | Out-Null
    $stations = @(Request $riderSession GET '/api/rider/stations')
    if (-not ($stations | Where-Object { $_.id -eq $stationId -and $_.favorite })) { throw 'Favorite was not persisted.' }
    foreach ($number in @(1,2)) {
        $issues += Request $riderSession POST '/api/rider/issues' @{ stationId=$stationId; description="Automated TEST report $number - safe to resolve" }
    }
    Request $riderSession POST "/api/rider-issues/$($issues[0].id)/resolve" $null 403 | Out-Null
    Request $admin POST "/api/rider-issues/$($issues[0].id)/resolve" | Out-Null
    $station = Request $admin GET "/api/stations/$stationId"
    if ($station.analysis.maintenanceStatus -ne 'OPEN_ISSUES') { throw 'Other unresolved issue was lost.' }
    Request $admin POST "/api/rider-issues/$($issues[1].id)/resolve" | Out-Null
    $savedIssues = @(Request $riderSession GET '/api/rider/issues')
    if (@($savedIssues | Where-Object { $_.id -in $issues.id -and $_.status -eq 'RESOLVED' }).Count -ne 2) { throw 'Resolution was not persisted.' }
    Request $riderSession DELETE "/api/rider/favorites/$stationId" $null 204 | Out-Null
    Request $riderSession POST '/api/auth/logout' $null 204 | Out-Null
    Request $riderSession GET '/api/rider/stations' $null 401 | Out-Null
    Write-Output 'PASS: registration, duplicate rejection, role safety, login/logout, favorites and multi-issue resolution. Provider counts were not modified.'
}
finally {
    foreach($issue in $issues) { Request $admin POST "/api/rider-issues/$($issue.id)/resolve" | Out-Null }
    Request $admin PATCH "/api/users/$($rider.id)/enabled" @{ enabled=$false } | Out-Null
}
