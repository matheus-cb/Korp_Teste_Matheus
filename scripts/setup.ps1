$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$exampleFile = Join-Path $repoRoot '.env.example'
$environmentFile = Join-Path $repoRoot '.env'

if (-not (Test-Path -LiteralPath $environmentFile)) {
    Copy-Item -LiteralPath $exampleFile -Destination $environmentFile
}

$content = [System.IO.File]::ReadAllText($environmentFile)
$configuredToken = [regex]::Match($content, '(?m)^INTERNAL_SERVICE_TOKEN=(.+)$').Groups[1].Value.Trim()
if ([string]::IsNullOrWhiteSpace($configuredToken)) {
    $bytes = New-Object byte[] 32
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $random.GetBytes($bytes)
    } finally {
        $random.Dispose()
    }

    $token = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    if ($content -match '(?m)^INTERNAL_SERVICE_TOKEN=') {
        $content = [regex]::Replace($content, '(?m)^INTERNAL_SERVICE_TOKEN=.*$', "INTERNAL_SERVICE_TOKEN=$token")
    } else {
        $content = $content.TrimEnd() + [Environment]::NewLine + "INTERNAL_SERVICE_TOKEN=$token" + [Environment]::NewLine
    }

    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($environmentFile, $content, $utf8)
    Write-Host 'Arquivo .env criado e token interno aleatório gerado.' -ForegroundColor Green
} else {
    Write-Host 'Arquivo .env já contém um token interno; nenhum segredo foi alterado.' -ForegroundColor Yellow
}
