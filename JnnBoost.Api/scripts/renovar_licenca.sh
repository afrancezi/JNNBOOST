#!/bin/sh
#
# renovar_licenca.sh
#
# Renova uma licença existente, estendendo o prazo de expiração e
# reativando o acesso (caso tenha sido revogado automaticamente por
# expiração).
#
# Uso:
#   sh renovar_licenca.sh
#   ou
#   ./renovar_licenca.sh

set -eu

CONTAINER_POSTGRES="postgres-licencas"
DB_USER="appuser"
DB_NAME="licencas_db"

echo "=========================================="
echo "      JNNBOOST - Renovar Licença"
echo "=========================================="
echo ""

printf "Chave da licença a renovar: "
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
printf "Quantos dias adicionar a partir de HOJE? "
read DIAS

case "$DIAS" in
    ''|*[!0-9]*)
        echo "Erro: informe um número inteiro de dias."
        exit 1
        ;;
esac

echo ""
echo "========== Resumo =========="
echo "Chave........: $CHAVE"
echo "Nova validade: $DIAS dias a partir de hoje"
echo "============================"
echo ""
echo "Atenção: o contador reinicia a partir de HOJE, não a partir da"
echo "expiração anterior."
echo ""

printf "Confirma a renovação? (s/N): "
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
SET ativa = true,
    data_expiracao = NOW() + INTERVAL '${DIAS} days'
WHERE chave_licenca = '$CHAVE';
SQL

echo ""
echo "=========================================="
echo "Licença renovada com sucesso!"
echo ""
docker exec -i "$CONTAINER_POSTGRES" psql -U "$DB_USER" -d "$DB_NAME" <<SQL
SELECT
    chave_licenca                      AS "Licença",
    CASE WHEN revogada THEN 'REVOGADA' WHEN ativa THEN 'ATIVA' ELSE 'INATIVA' END AS "Status",
    COALESCE(TO_CHAR(data_expiracao,'DD/MM/YYYY HH24:MI'), 'Nunca') AS "Expira em"
FROM licencas
WHERE chave_licenca = '$CHAVE';
SQL
echo "=========================================="
