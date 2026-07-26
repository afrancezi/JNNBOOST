#!/usr/bin/env bash
#
# renovar_licenca.sh
#
# Renova uma licença existente, estendendo o prazo de expiração e
# reativando o acesso (caso tenha sido revogado automaticamente).
#
# Uso: ./renovar_licenca.sh

set -euo pipefail

CONTAINER_POSTGRES="postgres-licencas"
DB_USER="appuser"
DB_NAME="licencas_db"

echo "=== Renovar licença existente ==="
echo ""

# --- Chave da licença a renovar ---
read -rp "Chave da licença a renovar: " CHAVE

if [[ -z "$CHAVE" ]]; then
    echo "Erro: a chave não pode ficar em branco."
    exit 1
fi

# --- Verifica se a licença existe antes de perguntar mais coisa ---
EXISTE=$(docker exec -i "$CONTAINER_POSTGRES" psql -U "$DB_USER" -d "$DB_NAME" -tAc \
    "SELECT COUNT(*) FROM licencas WHERE chave_licenca = '$CHAVE';")

if [[ "$EXISTE" -eq 0 ]]; then
    echo "Erro: nenhuma licença encontrada com a chave '$CHAVE'."
    exit 1
fi

# --- Mostra o estado atual da licença antes de renovar ---
echo ""
echo "Estado atual da licença:"
docker exec -i "$CONTAINER_POSTGRES" psql -U "$DB_USER" -d "$DB_NAME" -c "
SELECT chave_licenca, ativa, data_expiracao
FROM licencas
WHERE chave_licenca = '$CHAVE';
"

# --- Quantos dias adicionar ---
read -rp "Quantos dias adicionar a partir de hoje? " DIAS

if ! [[ "$DIAS" =~ ^[0-9]+$ ]]; then
    echo "Erro: informe um número inteiro de dias."
    exit 1
fi

# --- Confirmação ---
echo ""
echo "A licença '$CHAVE' será reativada, com nova expiração em $DIAS dias a partir de HOJE"
echo "(não a partir da expiração anterior - o contador reinicia a partir de agora)."
read -rp "Confirma a renovação? (s/N): " CONFIRMA

if [[ ! "$CONFIRMA" =~ ^[sS]$ ]]; then
    echo "Cancelado."
    exit 0
fi

# --- Atualiza no banco: reativa e estende o prazo ---
docker exec -i "$CONTAINER_POSTGRES" psql -U "$DB_USER" -d "$DB_NAME" -c "
UPDATE licencas
SET ativa = true,
    data_expiracao = NOW() + INTERVAL '${DIAS} days'
WHERE chave_licenca = '$CHAVE';
"

echo ""
echo "Licença '$CHAVE' renovada com sucesso!"
echo ""
echo "Novo estado:"
docker exec -i "$CONTAINER_POSTGRES" psql -U "$DB_USER" -d "$DB_NAME" -c "
SELECT chave_licenca, ativa, hwid, data_expiracao
FROM licencas
WHERE chave_licenca = '$CHAVE';
"
