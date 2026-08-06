#!/bin/sh
#
# tentativas_bloqueadas.sh
#
# Mostra quais licenças sofreram tentativa de acesso por HWID diferente
# do cadastrado (indício de compartilhamento de chave), com contagem
# e data da última tentativa.
#
# Uso:
#   sh tentativas_bloqueadas.sh
#   ou
#   ./tentativas_bloqueadas.sh

set -eu

CONTAINER_POSTGRES="postgres-licencas"
DB_USER="appuser"
DB_NAME="licencas_db"

echo "=========================================="
echo "   JNNBOOST - Tentativas Bloqueadas"
echo "=========================================="
echo ""

TOTAL=$(docker exec -i "$CONTAINER_POSTGRES" psql -U "$DB_USER" -d "$DB_NAME" -tAc \
    "SELECT COUNT(DISTINCT licenca_id) FROM tentativas_bloqueadas;")

echo "Total de licenças afetadas: $TOTAL"
echo ""

if [ "$TOTAL" -eq 0 ]; then
    echo "Nenhuma tentativa bloqueada registrada."
    echo "=========================================="
    exit 0
fi

docker exec -i "$CONTAINER_POSTGRES" psql -U "$DB_USER" -d "$DB_NAME" <<SQL
SELECT
    l.chave_licenca                              AS "Licença",
    COUNT(t.id)                                  AS "Tentativas",
    TO_CHAR(MAX(t.tentativa_em),'DD/MM/YYYY HH24:MI') AS "Última tentativa"
FROM tentativas_bloqueadas t
JOIN licencas l ON l.id = t.licenca_id
GROUP BY l.chave_licenca
ORDER BY COUNT(t.id) DESC;
SQL

echo ""
echo "=========================================="
echo "Fim da listagem."
echo "=========================================="
