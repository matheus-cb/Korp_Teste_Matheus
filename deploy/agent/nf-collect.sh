#!/usr/bin/env bash
# Coletor de estado. Roda como root, mas NAO e o agente: apenas escreve um
# retrato do sistema em texto. O agente le esse arquivo sem privilegio nenhum.
# Separar as duas coisas e o que impede o agente de precisar de root.
set -uo pipefail
umask 022
OUT=/var/lib/nfagent/estado.txt
COMPOSE="docker compose -f /opt/notaflow/docker-compose.prod.yml"

{
    echo "# Estado do NotaFlow - $(date -Is)"
    echo
    echo "## Conteineres"
    $COMPOSE ps --format '{{.Service}}\t{{.State}}\t{{.Health}}\t{{.Image}}' 2>&1

    echo
    echo "## Recursos do host"
    free -m | head -2
    df -h / | tail -1
    uptime
    echo "carga por conteiner:"
    docker stats --no-stream --format '{{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}' 2>&1

    echo
    echo "## Healthchecks internos"
    for probe in "billing http://127.0.0.1:5002/health/ready" "inventory http://127.0.0.1:5001/health"; do
        set -- $probe
        code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "$2" 2>/dev/null) || code=000
        [ "$code" = "000" ] && code="sem resposta"
        echo "$1: $code"
    done

    echo
    echo "## Borda publica"
    src=$(grep -E '^SITE_ADDRESS=' /opt/notaflow/.env | cut -d= -f2-)
    echo "SITE_ADDRESS=$src"
    caddy_code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "https://$src/healthz" 2>/dev/null) || caddy_code=000
    [ "$caddy_code" = "000" ] && caddy_code="sem resposta"
    echo "caddy: $caddy_code"

    echo
    echo "## Erros recentes nos logs (2h, sem dados sensiveis)"
    # INV-22: nunca extrair prompt, imagem, token ou credencial para o relatorio.
    $COMPOSE logs --since 2h --no-color 2>/dev/null \
        | grep -iE '\b(error|fail|exception|fatal|unhealthy)\b' \
        | grep -viE 'token|password|senha|apikey|api_key|authorization|bearer|prompt' \
        | tail -40

    echo
    echo "## Backups"
    ls -lh /opt/notaflow/backups 2>/dev/null | tail -5
    echo "timer: $(systemctl is-active notaflow-backup.timer)"

    echo
    echo "## Seguranca"
    echo "fail2ban: $(systemctl is-active fail2ban)"
    fail2ban-client status sshd 2>/dev/null | grep -E 'Currently banned|Total banned' || true
    echo "firewall: $(firewall-cmd --list-ports 2>/dev/null)"
    pendentes=$(dnf -q --security check-update 2>/dev/null | grep -c '^[a-zA-Z]') || true
    echo "updates de seguranca pendentes: ${pendentes:-0}"
} > "$OUT.tmp" 2>&1

mv "$OUT.tmp" "$OUT"
chown root:nfagent "$OUT"
chmod 640 "$OUT"
