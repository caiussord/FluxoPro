$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$envFile = Join-Path $projectRoot '.env'
if (-not (Test-Path -LiteralPath $envFile)) {
    throw 'Arquivo .env não encontrado. Copie .env.example para .env e configure os valores.'
}
Get-Content -LiteralPath $envFile | Where-Object { $_ -match '^[A-Z0-9_]+=' } | ForEach-Object {
    $entry = $_.Split('=', 2)
    [Environment]::SetEnvironmentVariable($entry[0], $entry[1], 'Process')
}
dotnet run --project (Join-Path $projectRoot 'src/FluxoPro.Api') --urls 'http://0.0.0.0:5187'
