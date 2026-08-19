# Popula a stack local com dados de demonstração em vários estados:
# catálogo variado, notas abertas, fechadas e uma rejeitada por saldo
# insuficiente. Idempotente por código de produto — rode quantas vezes quiser.
#
#   .\scripts\seed-demo.ps1
#   .\scripts\seed-demo.ps1 -BaseUrl http://127.0.0.1:4200

[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://127.0.0.1:4200'
)

$ErrorActionPreference = 'Stop'
$inventory = "$BaseUrl/inventory-api/api"
$billing = "$BaseUrl/billing-api/api"

function New-Product {
    param([string]$Code, [string]$Description, [int]$Balance)

    $body = @{ code = $Code; description = $Description; balance = $Balance } | ConvertTo-Json
    try {
        $created = Invoke-RestMethod "$inventory/products" -Method Post -ContentType 'application/json' -Body $body
        Write-Host ("  + {0,-16} {1,-34} saldo {2}" -f $created.code, $created.description, $created.balance)
        return $created
    }
    catch {
        # Código duplicado: recupera o existente para o script seguir idempotente.
        $existing = (Invoke-RestMethod "$inventory/products?query=$([uri]::EscapeDataString($Code))&page=1&pageSize=5").items |
            Where-Object { $_.code -eq $Code } | Select-Object -First 1
        if ($existing) {
            Write-Host ("  = {0,-16} ja existia (saldo {1})" -f $existing.code, $existing.balance)
            return $existing
        }
        throw
    }
}

function New-Invoice {
    param([array]$Items)

    $body = @{ items = $Items } | ConvertTo-Json -Depth 5
    return Invoke-RestMethod "$billing/invoices" -Method Post -ContentType 'application/json' -Body $body
}

function Close-Invoice {
    param([string]$Id)

    $response = Invoke-WebRequest "$billing/invoices/$Id/close" -Method Post -SkipHttpErrorCheck
    return @{ Status = $response.StatusCode; Body = ($response.Content | ConvertFrom-Json) }
}

Write-Host "`n=== Catalogo ===" -ForegroundColor Cyan
$catalog = @{}
$catalog['cabo']     = New-Product -Code 'CAB-USBC-2M'  -Description 'Cabo USB-C 2 m'              -Balance 140
$catalog['teclado']  = New-Product -Code 'TEC-SF-01'    -Description 'Teclado sem fio'            -Balance 42
$catalog['mouse']    = New-Product -Code 'MOU-OPT-05'   -Description 'Mouse optico 1600 DPI'      -Balance 18
$catalog['monitor']  = New-Product -Code 'MON-24-IPS'   -Description 'Monitor 24 polegadas IPS'   -Balance 7
$catalog['headset']  = New-Product -Code 'HEA-BT-330'   -Description 'Headset bluetooth'          -Balance 3
$catalog['dock']     = New-Product -Code 'DOC-USB-7P'   -Description 'Dock station 7 portas'      -Balance 1
$catalog['webcam']   = New-Product -Code 'WEB-HD-720'   -Description 'Webcam HD 720p'             -Balance 0
$catalog['suporte']  = New-Product -Code 'SUP-MON-ART'  -Description 'Suporte articulado monitor' -Balance 25
$catalog['hub']      = New-Product -Code 'HUB-HDMI-4K'  -Description 'Hub HDMI 4K'                -Balance 60
$catalog['cadeira']  = New-Product -Code 'CAD-ERG-PRO'  -Description 'Cadeira ergonomica'         -Balance 9

Write-Host "`n=== Notas fechadas ===" -ForegroundColor Cyan
$closedPlans = @(
    @(@{ productId = $catalog['cabo'].id; quantity = 4 }, @{ productId = $catalog['hub'].id; quantity = 2 }),
    @(@{ productId = $catalog['teclado'].id; quantity = 2 }),
    @(@{ productId = $catalog['suporte'].id; quantity = 3 }, @{ productId = $catalog['mouse'].id; quantity = 1 }),
    @(@{ productId = $catalog['cabo'].id; quantity = 10 }),
    @(@{ productId = $catalog['cadeira'].id; quantity = 1 }, @{ productId = $catalog['monitor'].id; quantity = 1 })
)
foreach ($plan in $closedPlans) {
    $invoice = New-Invoice -Items $plan
    $result = Close-Invoice -Id $invoice.id
    Write-Host ("  nota #{0,-4} {1} -> HTTP {2}" -f $invoice.number, $result.Body.status, $result.Status)
}

Write-Host "`n=== Notas abertas ===" -ForegroundColor Cyan
$openPlans = @(
    @(@{ productId = $catalog['monitor'].id; quantity = 2 }, @{ productId = $catalog['dock'].id; quantity = 1 }),
    @(@{ productId = $catalog['headset'].id; quantity = 2 }),
    @(@{ productId = $catalog['mouse'].id; quantity = 5 }, @{ productId = $catalog['cabo'].id; quantity = 3 }),
    @(@{ productId = $catalog['hub'].id; quantity = 8 })
)
foreach ($plan in $openPlans) {
    $invoice = New-Invoice -Items $plan
    Write-Host ("  nota #{0,-4} aberta com {1} item(ns)" -f $invoice.number, $plan.Count)
}

Write-Host "`n=== Nota rejeitada (saldo insuficiente) ===" -ForegroundColor Cyan
# Webcam esta zerada: o fechamento e rejeitado e a nota permanece aberta,
# exatamente o estado que a interface precisa saber exibir.
$rejected = New-Invoice -Items @(@{ productId = $catalog['webcam'].id; quantity = 5 })
$result = Close-Invoice -Id $rejected.id
Write-Host ("  nota #{0} -> HTTP {1} ({2})" -f $rejected.number, $result.Status, $result.Body.code)

Write-Host "`n=== Resumo ===" -ForegroundColor Cyan
$products = Invoke-RestMethod "$inventory/products?page=1&pageSize=100"
$invoices = Invoke-RestMethod "$billing/invoices?page=1&pageSize=100"
Write-Host ("  produtos: {0}" -f $products.items.Count)
Write-Host ("  notas:    {0} (fechadas {1}, abertas {2})" -f `
    $invoices.items.Count,
    ($invoices.items | Where-Object { $_.status -eq 'Closed' }).Count,
    ($invoices.items | Where-Object { $_.status -eq 'Open' }).Count)
Write-Host "`nPronto. Abra $BaseUrl`n" -ForegroundColor Green
