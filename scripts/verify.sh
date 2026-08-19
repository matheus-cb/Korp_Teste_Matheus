#!/usr/bin/env bash
# Validação do NotaFlow em camadas (ver AGENTS.md → "Comandos de validação").
#
#   ./scripts/verify.sh         Camada 1 — sem dependência externa
#   ./scripts/verify.sh --all   Camadas 1 a 3, avisando o que o ambiente não permite
#
# O script avisa em vez de falhar quando falta Docker ou Chromium: a ausência da
# ferramenta não é um defeito do código, e mascarar a diferença entre "não rodou"
# e "passou" é justamente o que produz relatório verde falso.
set -euo pipefail

run_all=0
[ "${1:-}" = "--all" ] && run_all=1

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"

skipped=()
step() { printf '\n\033[1m== %s\033[0m\n' "$1"; }

command -v dotnet >/dev/null 2>&1 || {
  echo "erro: .NET SDK não encontrado. Instale a versão de global.json." >&2
  exit 1
}

# O Angular CLI recusa Node < 22.22.3 e ainda assim sai com código 0; sem esta
# checagem o lint "passa" sem ter rodado.
node_version="$(node --version 2>/dev/null | tr -d 'v' || echo 0)"
node_ok=0
if [ "$(printf '%s\n22.22.3\n' "$node_version" | sort -V | head -1)" = "22.22.3" ]; then
  node_ok=1
fi

step "Camada 1 — convenção dos arquivos de contexto"
"$repo/scripts/check-agent-docs.sh"

step "Camada 1 — backend"
dotnet restore NotaFlow.slnx
dotnet build NotaFlow.slnx --no-restore
dotnet format NotaFlow.slnx --verify-no-changes --no-restore
dotnet test tests/billing/Billing.Api.Tests.csproj --no-build
dotnet test tests/inventory/Inventory.UnitTests/Inventory.UnitTests.csproj --no-build

step "Camada 1 — frontend"
if [ "$node_ok" -eq 1 ]; then
  (cd frontend && npm ci --no-audit --no-fund && npm run lint && npm run build:production)
else
  echo "aviso: Node $node_version é anterior ao mínimo 22.22.3 do Angular CLI."
  skipped+=("Camada 1 (frontend): Node $node_version < 22.22.3")
fi

if [ "$run_all" -eq 1 ]; then
  step "Camada 2 — testes do frontend"
  if [ "$node_ok" -eq 1 ] && [ -n "${CHROME_BIN:-}" ]; then
    (cd frontend && npm test)
  else
    echo "aviso: exige Node compatível e CHROME_BIN apontando para um Chromium."
    skipped+=("Camada 2: CHROME_BIN ausente ou Node incompatível")
  fi

  step "Camada 3 — integração e Compose"
  if docker info >/dev/null 2>&1; then
    dotnet test tests/billing/Billing.IntegrationTests/Billing.IntegrationTests.csproj --no-build
    dotnet test tests/inventory/Inventory.IntegrationTests/Inventory.IntegrationTests.csproj --no-build
    docker compose config --quiet
  else
    echo "aviso: daemon Docker indisponível; Testcontainers e Compose não rodam."
    skipped+=("Camada 3: daemon Docker indisponível")
  fi
fi

printf '\n'
if [ ${#skipped[@]} -eq 0 ]; then
  echo "Tudo que foi solicitado passou."
else
  echo "Passou, mas com camadas NÃO executadas — declare isto no relatório:"
  printf '  - %s\n' "${skipped[@]}"
fi
