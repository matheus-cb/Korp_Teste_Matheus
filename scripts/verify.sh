#!/usr/bin/env bash
# Validação do NotaFlow em camadas (ver AGENTS.md → "Comandos de validação").
#
#   ./scripts/verify.sh         Camada 1 — sem dependência externa
#   ./scripts/verify.sh --all   Camadas 1 a 3, avisando o que o ambiente não permite
#
# Códigos de saída:
#   0  tudo que foi pedido rodou e passou
#   1  alguma verificação falhou
#   3  passou, mas camadas OPCIONAIS ficaram por executar (2 e 3)
#
# O código 3 existe porque "avisa em vez de falhar" não pode virar verde para
# quem só lê o exit status. A Camada 1 nunca entra nessa conta: ela é
# obrigatória e faltar ferramenta para ela é falha, não aviso.
set -euo pipefail

run_all=0
[ "${1:-}" = "--all" ] && run_all=1

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"

skipped=()
step() { printf '\n\033[1m== %s\033[0m\n' "$1"; }
erro() { echo "erro: $1" >&2; exit 1; }

# Comparação de versão sem `sort -V`: a opção é do GNU coreutils e falta no
# `sort` do macOS/BSD, onde a checagem falharia justamente onde deveria proteger.
version_at_least() {
  local have="$1" want="$2" h w i
  local -a hp wp
  IFS=. read -r -a hp <<<"$have"
  IFS=. read -r -a wp <<<"$want"
  for i in 0 1 2; do
    h="${hp[i]:-0}"; w="${wp[i]:-0}"
    h="${h%%[!0-9]*}"; w="${w%%[!0-9]*}"
    h="${h:-0}"; w="${w:-0}"
    ((10#$h > 10#$w)) && return 0
    ((10#$h < 10#$w)) && return 1
  done
  return 0
}

# --- Ferramentas da Camada 1: ausência aqui é falha, não aviso ----------------
command -v dotnet >/dev/null 2>&1 ||
  erro ".NET SDK não encontrado. Instale a versão fixada em global.json."

command -v node >/dev/null 2>&1 ||
  erro "Node.js não encontrado. O Angular CLI exige 22.22.3 ou superior; o CI usa a 24."

node_version="$(node --version | tr -d 'v')"
version_at_least "$node_version" "22.22.3" ||
  erro "Node $node_version é anterior ao mínimo 22.22.3 do Angular CLI.
       Com esta versão a CLI recusa o comando e ainda sai com código 0, então
       pular o frontend aqui produziria exatamente o verde falso que a Camada 1
       existe para impedir."

step "Camada 1 — convenção dos arquivos de contexto"
"$repo/scripts/check-agent-docs.sh"

step "Camada 1 — backend"
dotnet restore NotaFlow.slnx
dotnet build NotaFlow.slnx --no-restore
dotnet format NotaFlow.slnx --verify-no-changes --no-restore
dotnet test tests/billing/Billing.Api.Tests.csproj --no-build
dotnet test tests/inventory/Inventory.UnitTests/Inventory.UnitTests.csproj --no-build

step "Camada 1 — frontend"
(cd frontend && npm ci --no-audit --no-fund && npm run lint && npm run build:production)

if [ "$run_all" -eq 1 ]; then
  step "Camada 2 — testes do frontend"
  if [ -n "${CHROME_BIN:-}" ]; then
    (cd frontend && npm test)
  else
    echo "aviso: CHROME_BIN não está definido; o Karma falharia por timeout sem dizer que o navegador nunca subiu."
    skipped+=("Camada 2: CHROME_BIN ausente")
  fi

  step "Camada 3 — integração e configuração do Compose"
  if docker info >/dev/null 2>&1; then
    dotnet test tests/billing/Billing.IntegrationTests/Billing.IntegrationTests.csproj --no-build
    dotnet test tests/inventory/Inventory.IntegrationTests/Inventory.IntegrationTests.csproj --no-build
    docker compose config --quiet
    # O smoke HTTP ponta a ponta (sobe a stack, faz login, fecha nota, confere
    # saldo e as rotas internas) roda só no quality-gate. Não e duplicado aqui
    # de proposito: seriam ~70 linhas de curl divergindo do CI em silencio.
    echo "nota: o smoke HTTP da stack é exclusivo do CI; aqui a Camada 3 cobre integração e a configuração do Compose."
  else
    echo "aviso: daemon Docker indisponível; Testcontainers e Compose não rodam."
    skipped+=("Camada 3: daemon Docker indisponível")
  fi
fi

printf '\n'
if [ ${#skipped[@]} -eq 0 ]; then
  echo "Tudo que foi solicitado passou."
  exit 0
fi

echo "Passou, mas com camadas NÃO executadas — declare isto no relatório:"
printf '  - %s\n' "${skipped[@]}"
exit 3
