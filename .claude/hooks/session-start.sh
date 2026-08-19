#!/usr/bin/env bash
# SessionStart — prepara a sessão remota do Claude Code.
#
# O contêiner remoto sobe sem .NET SDK e com um Node antigo demais para o
# Angular CLI. Sem este hook, um agente só descobre isso ao rodar o primeiro
# comando de validação — e, no caso do Angular, descobre errado: a CLI recusa a
# versão e mesmo assim sai com código 0.
#
# Docker fica de fora de propósito: não há daemon no contêiner, então a Camada 3
# do AGENTS.md não é executável aqui e nenhum hook conserta isso.
set -euo pipefail

# Só para sessões remotas: numa máquina local o ambiente é do desenvolvedor.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

repo="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
cd "$repo"

# Versão mínima exigida pelo Angular CLI; o CI usa a 24.
NODE_VERSION="v24.19.0"
NODE_DIR="$HOME/.local/node-$NODE_VERSION"
DOTNET_DIR="$HOME/.dotnet"
CHROME_WRAPPER="$HOME/.cache/notaflow/chrome-headless"

# Comparação de versão sem `sort -V`: a opção é do GNU coreutils e falta no
# `sort` do macOS/BSD, onde a checagem falharia e o Node correto não seria
# instalado.
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

# --- .NET SDK, na versão fixada em global.json --------------------------------
# Existir o binário não basta: com o global.json presente, `dotnet --version`
# resolve a versão pedida e falha se ela não estiver instalada. É o proprio
# resolvedor decidindo, então subir a versao fixada dispara a reinstalacao em
# vez de quebrar so no restore.
if ! ("$DOTNET_DIR/dotnet" --version >/dev/null 2>&1); then
  echo "hook: instalando o .NET SDK pedido por global.json"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "${TMPDIR:-/tmp}/dotnet-install.sh"
  bash "${TMPDIR:-/tmp}/dotnet-install.sh" --jsonfile global.json --install-dir "$DOTNET_DIR"
fi
export PATH="$DOTNET_DIR:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# --- Node, só se o do contêiner for velho demais ------------------------------
node_ok=0
if command -v node >/dev/null 2>&1 && version_at_least "$(node --version | tr -d 'v')" "22.22.3"; then
  node_ok=1
fi

if [ "$node_ok" -eq 0 ] && [ ! -x "$NODE_DIR/bin/node" ]; then
  echo "hook: Node do contêiner é antigo para o Angular CLI; instalando $NODE_VERSION"
  mkdir -p "$NODE_DIR"
  curl -fsSL "https://nodejs.org/dist/$NODE_VERSION/node-$NODE_VERSION-linux-x64.tar.xz" \
    -o "${TMPDIR:-/tmp}/node.tar.xz"
  tar -xJf "${TMPDIR:-/tmp}/node.tar.xz" -C "$NODE_DIR" --strip-components=1
fi
if [ -x "$NODE_DIR/bin/node" ]; then
  export PATH="$NODE_DIR/bin:$PATH"
fi

# --- Dependências do projeto --------------------------------------------------
# ci, não install: `npm install` reescreve o package-lock.json quando a versão do
# npm local difere da que o gerou, e a sessão começaria com o lockfile sujo — que
# um agente acaba commitando junto com a mudança real. `ci` nunca o escreve.
dotnet restore NotaFlow.slnx
(cd frontend && npm ci --no-audit --no-fund)

# --- Chromium para o Karma ----------------------------------------------------
# O Karma roda como root aqui, e o sandbox do Chromium exige usuário sem
# privilégio: sem --no-sandbox o navegador não sobe e o teste "falha" por
# timeout, escondendo a causa.
if [ -x /opt/pw-browsers/chromium ]; then
  mkdir -p "$(dirname "$CHROME_WRAPPER")"
  cat > "$CHROME_WRAPPER" <<'WRAPPER'
#!/usr/bin/env bash
exec /opt/pw-browsers/chromium --no-sandbox --disable-dev-shm-usage "$@"
WRAPPER
  chmod +x "$CHROME_WRAPPER"
fi

# --- Exporta para o resto da sessão -------------------------------------------
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo "export DOTNET_ROOT=\"$DOTNET_DIR\""
    echo "export PATH=\"$DOTNET_DIR:\$PATH\""
    echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
    echo "export DOTNET_NOLOGO=1"
    [ -x "$NODE_DIR/bin/node" ] && echo "export PATH=\"$NODE_DIR/bin:\$PATH\""
    [ -x "$CHROME_WRAPPER" ] && echo "export CHROME_BIN=\"$CHROME_WRAPPER\""
  } >> "$CLAUDE_ENV_FILE"
fi

echo "hook: pronto — dotnet $(dotnet --version), node $(node --version). Sem daemon Docker: Camada 3 indisponível."
