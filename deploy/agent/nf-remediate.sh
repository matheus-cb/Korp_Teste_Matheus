#!/usr/bin/env bash
# Allowlist de remediacao. O agente NAO executa comando livre: ele escolhe uma
# acao desta lista fechada, e este script (via sudo, so estas entradas) executa.
# Espelha a secao 5.2 do plano: o agente propoe, o servidor executa.
set -euo pipefail
COMPOSE="docker compose -f /opt/notaflow/docker-compose.prod.yml"
LOG=/var/log/nfagent-acoes.log

registrar() { echo "$(date -Is) | $*" >> "$LOG"; }

case "${1:-}" in
    reiniciar-servico)
        # So servicos sem estado. Banco NUNCA entra nesta lista.
        case "${2:-}" in
            billing|inventory|frontend|caddy) ;;
            *) echo "servico nao permitido: ${2:-}" >&2; exit 2 ;;
        esac
        registrar "reiniciando ${2}"
        $COMPOSE restart "$2"
        ;;
    limpar-imagens)
        registrar "prune de imagens antigas"
        docker image prune -f --filter "until=168h"
        ;;
    rodar-backup)
        registrar "backup sob demanda"
        /opt/notaflow/backup.sh
        ;;
    *)
        echo "acao desconhecida: ${1:-}" >&2
        echo "permitidas: reiniciar-servico <billing|inventory|frontend|caddy> | limpar-imagens | rodar-backup" >&2
        exit 2
        ;;
esac
