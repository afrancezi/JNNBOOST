#!/usr/bin/env bash
#
# revogar_licenca.sh
#
# Revoga permanentemente uma licença (ex: reembolso, banimento por
# compartilhamento de chave). Diferente de expiração: uma licença
# revogada NUNCA reativa sozinha, mesmo que alguém tente usá-la de novo.
#
# Uso: ./revogar_licenca.sh

set -euo pipefail

CONTAINER_POSTGRES="postgres-licencas"
DB_USER="appuser"
DB_NAME="licencas_db"

echo "=== Revogar licença ==="
echo ""

read -rp "Chave da licença a revogar: " CHAVE

if [[ -z "$CHAVE" ]]; then
    echo "Erro: a chave não pode ficar em branco."
    exit 1
fi

EXISTE=$(docker exec -i "$CONTAINER_POSTGRES" psql -U "$DB_USER" -d "$DB_NAME" -tAc \
    "SELECT COUNT(*) FROM licencas WHERE chave_licenca = '$CHAVE';")

if [[ "$EXISTE" -eq 0 ]]; then
    echo "Erro: nenhuma licença encontrada com a chave '$CHAVE'."
    exit 1
fi

echo ""
echo "Estado atual da licença:"
docker exec -i "$CONTAINER_POSTGRES" psql -U "$DB_USER" -d "$DB_NAME" -c "
SELECT chave_licenca, ativa, revogada, data_expiracao
FROM licencas
WHERE chave_licenca = '$CHAVE';
"

echo ""
read -rp "Motivo da revogação (opcional, só para seu controle): " MOTIVO
echo ""
echo "ATENÇÃO: essa ação é permanente. A licença nunca mais poderá ser"
echo "usada, mesmo com !renovarlicenca, a menos que você reverta manualmente."
read -rp "Confirma a REVOGAÇÃO de '$CHAVE'? (s/N): " CONFIRMA

if [[ ! "$CONFIRMA" =~ ^[sS]$ ]]; then
    echo "Cancelado."
    exit 0
fi

docker exec -i "$CONTAINER_POSTGRES" psql -U "$DB_USER" -d "$DB_NAME" -c "
UPDATE licencas
SET ativa = false,
    revogada = true
WHERE chave_licenca = '$CHAVE';
"

echo ""
echo "Licença '$CHAVE' revogada com sucesso."
if [[ -n "$MOTIVO" ]]; then
    echo "Motivo registrado (apenas neste terminal, não salvo no banco): $MOTIVO"
fi