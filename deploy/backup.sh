#!/usr/bin/env bash
# Sao luu CSDL SQLite an toan (khong can dung may chu) + giu 14 ban gan nhat; tuy chon day len Cloud Storage.
# Chay moi dem bang cron:  15 2 * * * /opt/kholanh/backup.sh
set -euo pipefail
DATA="${KHOLANH_DATA:-/var/lib/kholanh}"
OUT="${KHOLANH_BACKUP_DIR:-/var/backups/kholanh}"
KEEP="${KHOLANH_BACKUP_KEEP:-14}"
mkdir -p "$OUT"
STAMP="$(date +%Y%m%d-%H%M%S)"
FILE="$OUT/kho-$STAMP.db"
sqlite3 "$DATA/kho.db" ".backup '$FILE'"          # sudo apt install sqlite3
gzip -f "$FILE"
ls -1t "$OUT"/kho-*.db.gz 2>/dev/null | tail -n +$((KEEP + 1)) | xargs -r rm -f
# Tuy chon: gsutil cp "$FILE.gz" "gs://TEN-BUCKET/kholanh/"
echo "OK $FILE.gz"
