$ErrorActionPreference = 'Stop'

Write-Host 'Parando o serviço de Estoque...' -ForegroundColor Yellow
docker compose stop inventory
Write-Host 'Agora solicite o fechamento de uma nota aberta na interface.'
Read-Host 'Pressione Enter para religar o Estoque'
docker compose start inventory
Write-Host 'Estoque religado. A tentativa pendente deve ser reconciliada sem nova baixa.' -ForegroundColor Green

