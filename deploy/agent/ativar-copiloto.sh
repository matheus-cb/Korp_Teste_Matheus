#!/usr/bin/env bash
# Liga o Copiloto sobre a ponte, em um passo. Pede o token com digitacao
# oculta: ele nunca aparece no terminal nem no historico do shell.
#
# Uso:  scp deploy/agent/ativar-copiloto.sh hg-vps1:/tmp/
#       ssh -t hg-vps1 'bash /tmp/ativar-copiloto.sh'
#
# Rode o arquivo JA na VPS. Com `bash -s' < arquivo`, o stdin do bash e o
# proprio script, e o `read` abaixo consome uma linha dele em vez de esperar
# voce digitar -- grava um token vazio sem reclamar.
set -uo pipefail

# Checar o VALOR, nao o tamanho do arquivo: um arquivo com
# "CLAUDE_CODE_OAUTH_TOKEN=" e nada depois tem 25 bytes e passaria por -s.
ATUAL=$(grep -E '^(CLAUDE_CODE_OAUTH_TOKEN|ANTHROPIC_API_KEY)=' /etc/notaflow-agent.env 2>/dev/null | cut -d= -f2-)
# TROCAR=1 regrava mesmo havendo valor -- necessario quando o atual e invalido.
if [ -z "${ATUAL:-}" ] || [ "${TROCAR:-0}" = "1" ]; then
    printf 'Cole o token do Claude Code (claude setup-token) e Enter: '
    read -rs TOKEN
    echo
    if [ -z "$TOKEN" ]; then
        echo "[!] token vazio; nada foi alterado."
        echo "    Se voce colou e nada apareceu, e esperado: a digitacao e oculta."
        echo "    Se mesmo assim ficou vazio, rode o script direto na VPS,"
        echo "    nao por 'bash -s' com redirecionamento de arquivo."
        exit 1
    fi
    case "$TOKEN" in
        sk-ant-*) ;;
        *) echo "[!] isso nao parece um token (esperado comecar com sk-ant-)"; exit 1 ;;
    esac

    # A digitacao e oculta, entao nao ha retorno visual e e facil colar duas ou
    # dez vezes sem perceber. Um token colado N vezes vira um valor unico que a
    # API recusa com 401 -- erro que nao aponta para a causa.
    OCORRENCIAS=$(printf '%s' "$TOKEN" | grep -o 'sk-ant-' | wc -l)
    if [ "$OCORRENCIAS" -ne 1 ]; then
        echo "[!] o valor contem $OCORRENCIAS tokens grudados. Cole uma vez so."
        echo "    Dica: a digitacao e oculta de proposito; nao aparece nada na tela."
        exit 1
    fi

    TAMANHO=${#TOKEN}
    if [ "$TAMANHO" -lt 80 ] || [ "$TAMANHO" -gt 300 ]; then
        echo "[!] tamanho inesperado: $TAMANHO caracteres (um token tem ~108)."
        exit 1
    fi

    if printf '%s' "$TOKEN" | grep -qE '[[:space:]]'; then
        echo "[!] o valor tem espaco ou quebra de linha no meio."
        exit 1
    fi
    umask 077
    printf 'CLAUDE_CODE_OAUTH_TOKEN=%s\n' "$TOKEN" > /etc/notaflow-agent.env
    chown root:nfagent /etc/notaflow-agent.env
    chmod 640 /etc/notaflow-agent.env
    unset TOKEN
    echo "[+] credencial gravada"
else
    echo "[=] credencial ja presente"
fi

# A ponte roda como nfagent; 600 root:root deixaria o servico sem conseguir ler.
chown root:nfagent /etc/notaflow-agent.env
chmod 640 /etc/notaflow-agent.env
if ! runuser -u nfagent -- test -r /etc/notaflow-agent.env; then
    echo "[!] nfagent nao consegue ler a credencial"; exit 1
fi
# Provar que a credencial autentica ANTES de subir servico e ligar provedor.
# Sem isto, um 401 so aparece la na frente, disfarcado de erro da ponte.
echo "[*] conferindo a credencial contra a API..."
set -a; . /etc/notaflow-agent.env; set +a
TESTE=$(timeout 120 runuser -u nfagent -- env     CLAUDE_CODE_OAUTH_TOKEN="${CLAUDE_CODE_OAUTH_TOKEN:-}" HOME=/var/lib/nfagent     /var/lib/nfagent/.local/bin/claude -p "responda apenas: PONG"     --output-format json --model haiku --no-session-persistence < /dev/null 2>&1)
if printf '%s' "$TESTE" | grep -q '"is_error":true'; then
    echo "[!] a credencial nao autentica:"
    printf '%s' "$TESTE" | python3 -c 'import sys,json;print("   ",json.load(sys.stdin).get("result"))' 2>/dev/null         || printf '%s
' "$TESTE" | tail -c 300
    echo "    Gere um token novo: claude setup-token (na VPS, como root) e rode este script de novo."
    exit 1
fi
echo "[+] credencial valida"

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
