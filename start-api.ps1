param(
    [ValidateRange(1, 65535)][int]$Port = 5187,
    [string]$DatabasePath,
    [string]$WebhookUrl
)

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$envFile = Join-Path $projectRoot '.env'
if (-not (Test-Path -LiteralPath $envFile)) {
    throw 'Arquivo .env não encontrado. Copie .env.example para .env e configure os valores.'
}
Get-Content -LiteralPath $envFile | Where-Object { $_ -match '^[A-Z0-9_]+=' } | ForEach-Object {
    $entry = $_.Split('=', 2)
    [Environment]::SetEnvironmentVariable($entry[0], $entry[1], 'Process')
}
if ($DatabasePath) { $env:FLUXOPRO_DB_PATH = $DatabasePath }
if ($PSBoundParameters.ContainsKey('WebhookUrl')) { $env:FLUXOPRO_N8N_WEBHOOK_URL = $WebhookUrl }
dotnet run --project (Join-Path $projectRoot 'src/FluxoPro.Api') --urls "http://127.0.0.1:$Port"
