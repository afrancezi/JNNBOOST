#!/bin/sh
#
# revogar_licenca.sh
#
# Revoga permanentemente uma licença (ex: reembolso, banimento por
# compartilhamento de chave). Diferente de expiração: uma licença
# revogada NUNCA reativa sozinha, mesmo que alguém tente usá-la de novo.
#
# Uso:
#   sh revogar_licenca.sh
#   ou
#   ./revogar_licenca.sh

set -eu

CONTAINER_POSTGRES="postgres-licencas"
DB_USER="appuser"
DB_NAME="licencas_db"

echo "=========================================="
echo "      JNNBOOST - Revogar Licença"
echo "=========================================="
echo ""

printf "Chave da licença a revogar: "
read CHAVE

if [ -z "$CHAVE" ]; then
    echo "Erro: a chave não pode ficar em branco."
    exit 1
fi

EXISTE=$(docker exec -i "$CONTAINER_POSTGRES" psql -U "$DB_USER" -d "$DB_NAME" -tAc \
    "SELECT COUNT(*) FROM licencas WHERE chave_licenca = '$CHAVE';")

if [ "$EXISTE" -eq 0 ]; then
    echo "Erro: nenhuma licença encontrada com a chave '$CHAVE'."
    exit 1
fi

echo ""
echo "Estado atual:"
docker exec -i "$CONTAINER_POSTGRES" psql -U "$DB_USER" -d "$DB_NAME" <<SQL
SELECT
    chave_licenca                      AS "Licença",
    CASE WHEN revogada THEN 'REVOGADA' WHEN ativa THEN 'ATIVA' ELSE 'INATIVA' END AS "Status",
    COALESCE(TO_CHAR(data_expiracao,'DD/MM/YYYY HH24:MI'), 'Nunca') AS "Expira em"
FROM licencas
WHERE chave_licenca = '$CHAVE';
SQL

echo ""
printf "Motivo da revogação (opcional, só para seu controle): "
read MOTIVO

echo ""
echo "ATENÇÃO: essa ação é permanente. A licença nunca mais poderá ser"
echo "usada, mesmo com renovação, a menos que você reverta manualmente."
echo ""
printf "Confirma a REVOGAÇÃO de '$CHAVE'? (s/N): "
read CONFIRMA

case "$CONFIRMA" in
    s|S) ;;
    *)
        echo "Operação cancelada."
        exit 0
        ;;
esac

docker exec -i "$CONTAINER_POSTGRES" psql -U "$DB_USER" -d "$DB_NAME" <<SQL
UPDATE licencas
SET ativa = false,
    revogada = true
WHERE chave_licenca = '$CHAVE';
SQL

echo ""
echo "=========================================="
echo "Licença '$CHAVE' revogada com sucesso."
if [ -n "$MOTIVO" ]; then
    echo "Motivo (apenas neste terminal, não salvo no banco): $MOTIVO"
fi
echo "=========================================="
