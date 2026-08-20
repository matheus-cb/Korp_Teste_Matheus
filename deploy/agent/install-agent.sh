#!/usr/bin/env bash
# Instala o agente de operacao. Idempotente.
set -euo pipefail

# 1. Usuario sem privilegio. O agente nao e root e nao esta no grupo docker
#    (grupo docker equivale a root: qualquer membro monta / num conteiner).
if ! id nfagent >/dev/null 2>&1; then
    useradd --system --create-home --home-dir /var/lib/nfagent --shell /usr/sbin/nologin nfagent
    echo "[+] usuario nfagent criado"
else
    echo "[=] usuario nfagent ja existe"
fi
mkdir -p /var/lib/nfagent/relatorios
chown -R nfagent:nfagent /var/lib/nfagent
touch /var/log/nfagent-acoes.log && chown root:nfagent /var/log/nfagent-acoes.log && chmod 640 /var/log/nfagent-acoes.log

# 2. Scripts
install -m 755 -o root -g root nf-collect.sh    /usr/local/bin/nf-collect.sh
install -m 755 -o root -g root nf-remediate.sh  /usr/local/bin/nf-remediate.sh
install -m 750 -o nfagent -g nfagent nf-agent.sh /var/lib/nfagent/nf-agent.sh

# 3. sudo estreito: apenas o script de allowlist, nada mais.
cat > /etc/sudoers.d/nfagent <<'SUDO'
nfagent ALL=(root) NOPASSWD: /usr/local/bin/nf-remediate.sh
Defaults!/usr/local/bin/nf-remediate.sh !requiretty
SUDO
chmod 440 /etc/sudoers.d/nfagent
visudo -cf /etc/sudoers.d/nfagent >/dev/null && echo "[+] sudoers valido"

# 4. Claude Code para o nfagent (a instalacao do root nao e legivel por ele)
if [ ! -x /var/lib/nfagent/.local/bin/claude ]; then
    echo "[*] instalando Claude Code para o nfagent..."
    runuser -u nfagent -- bash -c 'curl -fsSL https://claude.ai/install.sh | bash' >/dev/null 2>&1 \
        || echo "[!] instalacao automatica falhou; instale manualmente"
fi
runuser -u nfagent -- /var/lib/nfagent/.local/bin/claude --version 2>/dev/null \
    && echo "[+] claude disponivel para nfagent" || echo "[!] claude ainda indisponivel para nfagent"

# 5. Unidades systemd
cat > /etc/systemd/system/notaflow-collect.service <<'UNIT'
[Unit]
Description=Coleta o estado do NotaFlow
After=docker.service

[Service]
Type=oneshot
ExecStart=/usr/local/bin/nf-collect.sh
UNIT

cat > /etc/systemd/system/notaflow-agent.service <<'UNIT'
[Unit]
Description=Agente de operacao do NotaFlow (Claude Code)
After=notaflow-collect.service
Requires=notaflow-collect.service

[Service]
Type=oneshot
User=nfagent
Group=nfagent
EnvironmentFile=/etc/notaflow-agent.env
WorkingDirectory=/var/lib/nfagent
ExecStart=/var/lib/nfagent/nf-agent.sh
TimeoutStartSec=900

# Endurecimento: o agente enxerga o minimo do sistema.
NoNewPrivileges=no
PrivateTmp=yes
ProtectSystem=strict
ProtectHome=yes
ReadWritePaths=/var/lib/nfagent
ProtectKernelTunables=yes
ProtectKernelModules=yes
ProtectControlGroups=yes
RestrictSUIDSGID=yes
LockPersonality=yes
MemoryMax=400M

[Install]
WantedBy=multi-user.target
UNIT

cat > /etc/systemd/system/notaflow-agent.timer <<'UNIT'
[Unit]
Description=Roda o agente de operacao periodicamente

[Timer]
OnBootSec=10min
OnUnitActiveSec=6h
RandomizedDelaySec=5min
Persistent=true

[Install]
WantedBy=timers.target
UNIT

systemctl daemon-reload
# O coletor roda junto com o agente (Requires), mas tambem sozinho a cada hora,
# para haver retrato recente quando alguem for olhar.
systemctl enable --now notaflow-agent.timer >/dev/null 2>&1
echo "[+] timer do agente: $(systemctl is-active notaflow-agent.timer)"
systemctl list-timers 'notaflow-*' --no-pager 2>/dev/null | head -5
