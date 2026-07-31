#!/usr/bin/env bash
#
# licencas_ativas.sh
#
# Mostra a quantidade e a lista de licenças ativas no momento, direto
# no banco de dados (sem depender da API ou do bot do Discord).
#
# Uso: ./licencas_ativas.sh

set -euo pipefail

CONTAINER_POSTGRES="postgres-licencas"
DB_USER="appuser"
DB_NAME="licencas_db"

echo "=== Licenças ativas ==="
echo ""

TOTAL=$(docker exec -i "$CONTAINER_POSTGRES" psql -U "$DB_USER" -d "$DB_NAME" -tAc \
    "SELECT COUNT(*) FROM licencas WHERE ativa = true;")

echo "Total de licenças ativas: $TOTAL"
echo ""

if [[ "$TOTAL" -gt 0 ]]; then
    echo "Detalhes:"
    docker exec -i "$CONTAINER_POSTGRES" psql -U "$DB_USER" -d "$DB_NAME" -c "
    SELECT chave_licenca, hwid, data_ativacao, data_expiracao
    FROM licencas
    WHERE ativa = true
    ORDER BY data_expiracao NULLS LAST;
    "
fi
