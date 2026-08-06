#!/bin/sh
#
# backup_banco.sh
#
# Faz um dump comprimido do banco de licenças e apaga backups com mais
# de 30 dias, para não acumular espaço em disco indefinidamente.
#
# Pensado para rodar via cron (ex: todo dia às 3h da manhã).
#
# Uso manual:
#   sh backup_banco.sh
#   ou
#   ./backup_banco.sh

set -eu

CONTAINER_POSTGRES="postgres-licencas"
DB_USER="appuser"
DB_NAME="licencas_db"

DIR_BACKUP="$HOME/backups-jnnboost"
DIAS_RETENCAO=30

mkdir -p "$DIR_BACKUP"

DATA=$(date +%Y%m%d_%H%M%S)
ARQUIVO="$DIR_BACKUP/licencas_db_${DATA}.sql.gz"

echo "=========================================="
echo "       JNNBOOST - Backup do Banco"
echo "=========================================="
echo ""
echo "Iniciando backup de '$DB_NAME'..."

docker exec -i "$CONTAINER_POSTGRES" pg_dump -U "$DB_USER" "$DB_NAME" | gzip > "$ARQUIVO"

TAMANHO=$(du -h "$ARQUIVO" | cut -f1)
echo "Backup salvo em: $ARQUIVO ($TAMANHO)"
echo ""

echo "Removendo backups com mais de $DIAS_RETENCAO dias..."
find "$DIR_BACKUP" -name "licencas_db_*.sql.gz" -mtime "+$DIAS_RETENCAO" -print -delete

echo ""
echo "Backups atuais:"
ls -lh "$DIR_BACKUP"

echo ""
echo "=========================================="
echo "Backup concluído."
echo "=========================================="

# -----------------------------------------------------------------
# COMO AGENDAR VIA CRON (rodar uma vez, manualmente, para configurar):
#
#   crontab -e
#
# Adicione a linha abaixo (ajuste o caminho do script conforme o seu):
#
#   0 3 * * * /home/SEU_USUARIO/JNNBOOST/JnnBoost.Api/scripts/backup_banco.sh >> /home/SEU_USUARIO/backups-jnnboost/backup.log 2>&1
# -----------------------------------------------------------------
