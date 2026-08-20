#!/usr/bin/env bash
# Prepara /opt/notaflow na VPS. Idempotente: rodar de novo nao regenera segredo
# ja existente nem derruba a stack.
set -euo pipefail

APP_DIR=/opt/notaflow
SITE_ADDRESS="${SITE_ADDRESS:-143-95-221-82.nip.io}"
PUBLIC_ORIGIN="${PUBLIC_ORIGIN:-https://$SITE_ADDRESS}"
OWNER="${OWNER:-matheus-cb}"

mkdir -p "$APP_DIR"
cd "$APP_DIR"

# Segredos nascem no servidor e nunca saem dele (secao 9.3 do plano).
gen() { tr -dc 'A-Za-z0-9' </dev/urandom | head -c 40; }

if [ ! -f .env ]; then
    umask 077
    cat > .env <<EOF
SITE_ADDRESS=$SITE_ADDRESS
PUBLIC_ORIGIN=$PUBLIC_ORIGIN
PUBLIC_HOST=$SITE_ADDRESS

IMAGE_INVENTORY=ghcr.io/$OWNER/notaflow-inventory:latest
IMAGE_BILLING=ghcr.io/$OWNER/notaflow-billing:latest
IMAGE_FRONTEND=ghcr.io/$OWNER/notaflow-frontend:latest

POSTGRES_INVENTORY_PASSWORD=$(gen)
POSTGRES_BILLING_PASSWORD=$(gen)
INTERNAL_SERVICE_TOKEN=$(gen)
SEED_PASSWORD=$(gen)

OPENAI_API_KEY=
OPENAI_MODEL=gpt-5.6-luna
EOF
    echo "[+] .env criado com segredos novos"
else
    echo "[=] .env ja existe; preservado"
fi
chmod 600 .env

# Backup diario dos dois volumes PostgreSQL (secao 9.4: hoje um `down -v` perde tudo).
mkdir -p "$APP_DIR/backups"
cat > "$APP_DIR/backup.sh" <<'BACKUP'
#!/usr/bin/env bash
set -euo pipefail
cd /opt/notaflow
stamp=$(date +%Y%m%d-%H%M)
for db in inventory billing; do
    docker compose -f docker-compose.prod.yml exec -T "$db-db" \
        pg_dump -U "$db" -d "$db" | gzip > "backups/$db-$stamp.sql.gz"
done
# Retencao de 14 dias.
find backups -name '*.sql.gz' -mtime +14 -delete
BACKUP
chmod +x "$APP_DIR/backup.sh"

cat > /etc/systemd/system/notaflow-backup.service <<'UNIT'
[Unit]
Description=Backup dos bancos do NotaFlow
After=docker.service

[Service]
Type=oneshot
ExecStart=/opt/notaflow/backup.sh
UNIT

cat > /etc/systemd/system/notaflow-backup.timer <<'UNIT'
[Unit]
Description=Backup diario do NotaFlow

[Timer]
OnCalendar=*-*-* 04:30:00
Persistent=true

[Install]
WantedBy=timers.target
UNIT

systemctl daemon-reload
systemctl enable --now notaflow-backup.timer >/dev/null 2>&1

echo "[+] pronto em $APP_DIR"
ls -la "$APP_DIR"
echo "[+] timer de backup: $(systemctl is-active notaflow-backup.timer)"
