#!/usr/bin/env bash
#
# backup_banco.sh
#
# Faz um dump comprimido do banco de licenças e apaga backups com mais
# de 30 dias, para não acumular espaço em disco indefinidamente.
#
# Pensado para rodar via cron (ex: todo dia às 3h da manhã).
#
# Uso manual: ./backup_banco.sh
# Uso via cron: veja instruções no final deste arquivo.

set -euo pipefail

CONTAINER_POSTGRES="postgres-licencas"
DB_USER="appuser"
DB_NAME="licencas_db"

# Pasta onde os backups ficam salvos. Ajuste se quiser outro local
# (ex: um disco separado, ou uma pasta sincronizada externamente).
DIR_BACKUP="$HOME/backups-jnnboost"
DIAS_RETENCAO=30

mkdir -p "$DIR_BACKUP"

DATA=$(date +%Y%m%d_%H%M%S)
ARQUIVO="$DIR_BACKUP/licencas_db_${DATA}.sql.gz"

echo "Iniciando backup de '$DB_NAME'..."

docker exec -i "$CONTAINER_POSTGRES" pg_dump -U "$DB_USER" "$DB_NAME" | gzip > "$ARQUIVO"

TAMANHO=$(du -h "$ARQUIVO" | cut -f1)
echo "Backup salvo em: $ARQUIVO ($TAMANHO)"

# Remove backups mais antigos que DIAS_RETENCAO dias
echo "Removendo backups com mais de $DIAS_RETENCAO dias..."
find "$DIR_BACKUP" -name "licencas_db_*.sql.gz" -mtime "+$DIAS_RETENCAO" -print -delete

echo "Backups atuais:"
ls -lh "$DIR_BACKUP"

# -----------------------------------------------------------------
# COMO AGENDAR VIA CRON (rodar uma vez, manualmente, para configurar):
#
#   crontab -e
#
# Adicione a linha abaixo (ajuste o caminho do script conforme o seu):
#
#   0 3 * * * /home/SEU_USUARIO/JNNBOOST/JnnBoost.Api/scripts/backup_banco.sh >> /home/SEU_USUARIO/backups-jnnboost/backup.log 2>&1
#
# Isso roda o backup todo dia às 3h da manhã, salvando a saída em um
# log para conferência posterior.
# -----------------------------------------------------------------