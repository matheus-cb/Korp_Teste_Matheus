$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    $candidate = 'C:\Users\matheus\.dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $candidate) {
        $dotnet = Get-Item -LiteralPath $candidate
    } else {
        throw '.NET 10 SDK não encontrado.'
    }
}

Push-Location $repoRoot
try {
    & $dotnet.Source restore NotaFlow.slnx
    & $dotnet.Source build NotaFlow.slnx --configuration Release --no-restore
    & $dotnet.Source test NotaFlow.slnx --configuration Release --no-build
    & $dotnet.Source format NotaFlow.slnx --verify-no-changes --no-restore

    Push-Location frontend
    try {
        npm ci
        npm run lint
        npm test
        npm run build:production
    } finally {
        Pop-Location
    }
} finally {
    Pop-Location
}

Write-Host 'Quality gate local concluído.' -ForegroundColor Green
