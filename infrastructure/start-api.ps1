param()
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$environmentFile = Join-Path $repositoryRoot '.env'
if (-not $env:ConnectionStrings__Database) {
    if (-not (Test-Path -LiteralPath $environmentFile)) {
        throw 'Copy .env.example to .env and set a local password, or set ConnectionStrings__Database.'
    }
    $settings = @{}
    foreach ($line in Get-Content -LiteralPath $environmentFile) {
        if ($line -match '^\s*(POSTGRES_DB|POSTGRES_USER|POSTGRES_PASSWORD|POSTGRES_PORT)=(.*)$') {
            $settings[$matches[1]] = $matches[2].Trim()
        }
    }
    foreach ($required in 'POSTGRES_DB','POSTGRES_USER','POSTGRES_PASSWORD','POSTGRES_PORT') {
        if (-not $settings[$required]) { throw "Missing $required in .env" }
    }
    # Quote ADO.NET values, including embedded delimiters; never evaluate .env as code.
    function Quote-ConnectionValue([string] $value) { '"' + $value.Replace('"', '""') + '"' }
    $env:ConnectionStrings__Database = 'Host=127.0.0.1;Port=' + $settings.POSTGRES_PORT +
        ';Database=' + (Quote-ConnectionValue $settings.POSTGRES_DB) +
        ';Username=' + (Quote-ConnectionValue $settings.POSTGRES_USER) +
        ';Password=' + (Quote-ConnectionValue $settings.POSTGRES_PASSWORD) + ';Timeout=5'
}
$localSdk = Join-Path $repositoryRoot '.tools/dotnet/dotnet.exe'
$dotnetExecutable = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
if (Test-Path -LiteralPath $localSdk) {
    $env:DOTNET_CLI_HOME = Join-Path $repositoryRoot '.tools/cli'
    $env:NUGET_PACKAGES = Join-Path $repositoryRoot '.tools/nuget'
}
Push-Location (Join-Path $repositoryRoot 'backend')
try {
    & $dotnetExecutable run --project src/Elio.Api --launch-profile http
    if ($LASTEXITCODE -ne 0) { throw 'API process exited unsuccessfully.' }
} finally { Pop-Location }
