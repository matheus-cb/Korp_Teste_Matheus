#!/usr/bin/env bash
# Instala a ponte de inferencia. Idempotente.
# A ponte roda como nfagent (sem privilegio, fora do grupo docker) e escuta
# apenas em 127.0.0.1. Ela nao recebe o INTERNAL_SERVICE_TOKEN: MCP e validacao
# continuam no Billing.
set -euo pipefail

BRIDGE_DIR=/var/lib/nfagent/bridge
mkdir -p "$BRIDGE_DIR"
install -m 640 -o nfagent -g nfagent server.js "$BRIDGE_DIR/server.js"

# O modulo padrao do AlmaLinux 9 traz Node 16, muito abaixo do minimo do
# projeto (>= 22.22.3, ver AGENTS.md). Habilitar o fluxo 22 explicitamente.
versao_node() { node --version 2>/dev/null | sed 's/^v//' | cut -d. -f1; }
if [ "$(versao_node || echo 0)" -lt 22 ] 2>/dev/null; then
    echo "[*] instalando Node.js 22..."
    dnf module reset -y nodejs >/dev/null 2>&1 || true
    dnf module enable -y nodejs:22 >/dev/null 2>&1 || true
    dnf install -y -q nodejs --allowerasing
fi
if [ "$(versao_node || echo 0)" -lt 22 ] 2>/dev/null; then
    echo "[!] Node ainda abaixo de 22: $(node --version 2>/dev/null || echo ausente)"
    exit 1
fi
echo "[+] node $(node --version)"

# Segredo compartilhado entre Billing e ponte, gerado no servidor.
if [ ! -f /etc/notaflow-bridge.env ]; then
    umask 077
    printf 'BRIDGE_SECRET=%s\n' "$(tr -dc 'A-Za-z0-9' </dev/urandom | head -c 48)" > /etc/notaflow-bridge.env
    echo "[+] segredo da ponte gerado"
else
    echo "[=] segredo da ponte preservado"
fi
chmod 640 /etc/notaflow-bridge.env
chown root:nfagent /etc/notaflow-bridge.env

# O container do Billing nao alcanca o loopback do host. Descobrir o IP do
# gateway da bridge do Docker e vincular a ponte nele: endereco privado, visivel
# so de dentro da VPS.
GATEWAY=$(ip -4 addr show docker0 2>/dev/null | awk '/inet /{print $2}' | cut -d/ -f1)
GATEWAY=${GATEWAY:-127.0.0.1}
printf 'BRIDGE_HOST=%s
' "$GATEWAY" > /etc/notaflow-bridge-host.env
echo "[+] ponte vai vincular em $GATEWAY"

cat > /etc/systemd/system/notaflow-bridge.service <<'UNIT'
[Unit]
Description=Ponte de inferencia do Copiloto NotaFlow
Wants=network-online.target
After=network-online.target docker.service
Requires=docker.service

[Service]
Type=simple
User=nfagent
Group=nfagent
WorkingDirectory=/var/lib/nfagent/bridge
# Credencial do Claude Code e segredo da ponte, ambos 640 e fora do repositorio.
EnvironmentFile=/etc/notaflow-agent.env
EnvironmentFile=/etc/notaflow-bridge.env
Environment=BRIDGE_PORT=5099
EnvironmentFile=/etc/notaflow-bridge-host.env
Environment=BRIDGE_MODEL=sonnet
Environment=CLAUDE_BIN=/var/lib/nfagent/.local/bin/claude
ExecStart=/usr/bin/node /var/lib/nfagent/bridge/server.js
Restart=always
RestartSec=5

# O endereço do gateway Docker só existe depois que a rede do daemon é criada.
# Esperar aqui evita a corrida de boot que gerava EADDRNOTAVAIL e deixava o
# assistente indisponível até um reinício posterior do serviço.
ExecStartPre=/bin/sh -c 'for i in $(seq 1 30); do /usr/sbin/ip -4 addr show docker0 2>/dev/null | /usr/bin/grep -q "inet " && exit 0; /usr/bin/sleep 1; done; exit 1'

# O harness roda aqui dentro: limitar o que ele alcanca.
NoNewPrivileges=yes
PrivateTmp=yes
ProtectSystem=strict
ProtectHome=yes
ReadWritePaths=/var/lib/nfagent
ProtectKernelTunables=yes
ProtectKernelModules=yes
RestrictSUIDSGID=yes
LockPersonality=yes
MemoryMax=700M

[Install]
WantedBy=multi-user.target
UNIT

systemctl daemon-reload
systemctl enable notaflow-bridge.service >/dev/null 2>&1
if [ -s /etc/notaflow-agent.env ]; then
    systemctl restart notaflow-bridge.service
    sleep 2
    echo "[+] ponte: $(systemctl is-active notaflow-bridge.service)"
    curl -s --max-time 5 http://127.0.0.1:5099/health || echo "(sem resposta ainda)"
else
    echo "[!] /etc/notaflow-agent.env vazio: a ponte so sobe com a credencial do Claude Code"
fi
echo "[=] segredo ja esta em /opt/notaflow/.env como CLAUDE_BRIDGE_SECRET" 
