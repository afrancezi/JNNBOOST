#!/usr/bin/env bash
#
# listar_licencas.sh
#
# Lista todas as licenças cadastradas no banco.
#
# Uso:
#   ./listar_licencas.sh

set -euo pipefail

CONTAINER_POSTGRES="postgres-licencas"
DB_USER="appuser"
DB_NAME="licencas_db"

echo "=========================================="
echo "       JNNBOOST - Lista de Licenças"
echo "=========================================="
echo ""

TOTAL=$(docker exec -i "$CONTAINER_POSTGRES" \
    psql -U "$DB_USER" -d "$DB_NAME" -tAc \
    "SELECT COUNT(*) FROM licencas;")

echo "Total de licenças cadastradas: $TOTAL"
echo ""

if [[ "$TOTAL" -eq 0 ]]; then
    echo "Nenhuma licença encontrada."
    exit 0
fi

docker exec -i "$CONTAINER_POSTGRES" \
    psql -U "$DB_USER" -d "$DB_NAME" <<'SQL'

SELECT
    chave_licenca                               AS "Licença",

    CASE
        WHEN revogada THEN 'REVOGADA'
        WHEN ativa THEN 'ATIVA'
        ELSE 'INATIVA'
    END                                         AS "Status",

    CASE
        WHEN hwid IS NULL OR hwid = '' OR hwid = '0'
            THEN '-'
        ELSE 'Registrado'
    END                                         AS "HWID",

    COALESCE(
        TO_CHAR(data_ativacao,'DD/MM/YYYY HH24:MI'),
        '-'
    )                                           AS "Ativada em",

    COALESCE(
        TO_CHAR(data_expiracao,'DD/MM/YYYY HH24:MI'),
        'Nunca'
    )                                           AS "Expira em",

    COALESCE(
        TO_CHAR(criada_em,'DD/MM/YYYY HH24:MI'),
        '-'
    )                                           AS "Criada em"

FROM licencas
ORDER BY criada_em DESC;

SQL

echo ""
echo "=========================================="
echo "Fim da listagem."
echo "=========================================="
