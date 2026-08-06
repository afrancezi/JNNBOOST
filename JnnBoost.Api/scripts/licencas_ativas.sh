#!/bin/sh
#
# licencas_ativas.sh
#
# Mostra a quantidade e a lista de licenças ativas no momento.
#
# Uso:
#   sh licencas_ativas.sh
#   ou
#   ./licencas_ativas.sh

set -eu

CONTAINER_POSTGRES="postgres-licencas"
DB_USER="appuser"
DB_NAME="licencas_db"

echo "=========================================="
echo "      JNNBOOST - Licenças Ativas"
echo "=========================================="
echo ""

TOTAL=$(docker exec -i "$CONTAINER_POSTGRES" psql -U "$DB_USER" -d "$DB_NAME" -tAc \
    "SELECT COUNT(*) FROM licencas WHERE ativa = true;")

echo "Total de licenças ativas: $TOTAL"
echo ""

if [ "$TOTAL" -eq 0 ]; then
    echo "Nenhuma licença ativa no momento."
    echo "=========================================="
    exit 0
fi

docker exec -i "$CONTAINER_POSTGRES" psql -U "$DB_USER" -d "$DB_NAME" <<SQL
SELECT
    chave_licenca                                       AS "Licença",
    CASE WHEN hwid IS NULL OR hwid = '' THEN '-' ELSE 'Registrado' END AS "HWID",
    COALESCE(TO_CHAR(data_ativacao,'DD/MM/YYYY HH24:MI'), '-')   AS "Ativada em",
    COALESCE(TO_CHAR(data_expiracao,'DD/MM/YYYY HH24:MI'), 'Nunca') AS "Expira em"
FROM licencas
WHERE ativa = true
ORDER BY data_expiracao NULLS LAST;
SQL

echo ""
echo "=========================================="
echo "Fim da listagem."
echo "=========================================="
