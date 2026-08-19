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

# --- .NET SDK, na versão fixada em global.json --------------------------------
if [ ! -x "$DOTNET_DIR/dotnet" ]; then
  echo "hook: instalando o .NET SDK de global.json"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "${TMPDIR:-/tmp}/dotnet-install.sh"
  bash "${TMPDIR:-/tmp}/dotnet-install.sh" --jsonfile global.json --install-dir "$DOTNET_DIR"
fi
export PATH="$DOTNET_DIR:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# --- Node, só se o do contêiner for velho demais ------------------------------
node_ok=0
if command -v node >/dev/null 2>&1; then
  # 22.22.3 é o piso do Angular CLI; comparação por ordenação de versão.
  current="$(node --version | tr -d 'v')"
  if [ "$(printf '%s\n22.22.3\n' "$current" | sort -V | head -1)" = "22.22.3" ]; then
    node_ok=1
  fi
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
