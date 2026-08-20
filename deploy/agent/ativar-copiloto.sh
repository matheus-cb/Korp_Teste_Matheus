#!/usr/bin/env bash
# Liga o Copiloto sobre a ponte, em um passo. Pede o token com digitacao
# oculta: ele nunca aparece no terminal nem no historico do shell.
#
# Uso:  ssh -t hg-vps1 'bash -s' < deploy/agent/ativar-copiloto.sh
set -uo pipefail

if [ ! -s /etc/notaflow-agent.env ]; then
    printf 'Cole o token do Claude Code (claude setup-token) e Enter: '
    read -rs TOKEN
    echo
    if [ -z "$TOKEN" ]; then echo "[!] token vazio; nada foi alterado"; exit 1; fi
    umask 077
    printf 'CLAUDE_CODE_OAUTH_TOKEN=%s\n' "$TOKEN" > /etc/notaflow-agent.env
    chmod 640 /etc/notaflow-agent.env
    chown root:nfagent /etc/notaflow-agent.env
    unset TOKEN
    echo "[+] credencial gravada"
else
    echo "[=] credencial ja existe"
fi

echo "[*] subindo a ponte..."
systemctl restart notaflow-bridge.service
sleep 3
if ! curl -sf --max-time 8 "http://$(grep '^BRIDGE_HOST=' /etc/notaflow-bridge-host.env | cut -d= -f2-):5099/health" >/dev/null; then
    echo "[!] a ponte nao respondeu; veja: journalctl -u notaflow-bridge -n 30"
    exit 1
fi
echo "[+] ponte no ar"

echo "[*] verificando que o Claude Code autentica..."
SEGREDO=$(grep '^BRIDGE_SECRET=' /etc/notaflow-bridge.env | cut -d= -f2-)
HOST=$(grep '^BRIDGE_HOST=' /etc/notaflow-bridge-host.env | cut -d= -f2-)
RESPOSTA=$(curl -s --max-time 120 -H 'Content-Type: application/json' \
    -d "{\"segredo\":\"$SEGREDO\",\"prompt\":\"Responda apenas com este JSON, nada mais: {\\\"acao\\\":\\\"teste\\\",\\\"ok\\\":true}\"}" \
    "http://$HOST:5099/draft")
unset SEGREDO
case "$RESPOSTA" in
    *'"ok"'*|*'ok'*true*) echo "[+] o modelo respondeu" ;;
    *) echo "[!] a ponte respondeu, mas o modelo nao: ${RESPOSTA:0:200}"; exit 1 ;;
esac

echo "[*] ligando o provedor no Billing..."
cd /opt/notaflow
sed -i 's|^AI_PROVIDER=.*|AI_PROVIDER=claude-bridge|' .env
grep -q '^AI_PROVIDER=' .env || echo 'AI_PROVIDER=claude-bridge' >> .env
docker compose -f docker-compose.prod.yml up -d billing >/dev/null 2>&1

for _ in $(seq 1 30); do
    estado=$(docker compose -f docker-compose.prod.yml ps --format '{{.Service}} {{.Health}}' | awk '$1=="billing"{print $2}')
    [ "$estado" = "healthy" ] && break
    sleep 4
done
echo "[+] billing: $estado"
echo
echo "Pronto. Abra o Assistente em https://143-95-221-82.nip.io e peca um pedido em texto."
echo "Imagem nao e suportada por este provedor: o atalho responde AI_IMAGE_UNSUPPORTED."
