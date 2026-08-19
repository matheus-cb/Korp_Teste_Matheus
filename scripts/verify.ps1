$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    # Instalação por usuário, onde o dotnet-install coloca o SDK fora do PATH.
    $candidates = @(
        (Join-Path $HOME '.dotnet/dotnet.exe'),
        (Join-Path $HOME '.dotnet/dotnet')
    )
    $found = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ($found) {
        $dotnet = Get-Item -LiteralPath $found
    } else {
        throw '.NET SDK nao encontrado. Instale a versao fixada em global.json.'
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
