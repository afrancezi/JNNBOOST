#!/usr/bin/env bash
#
# criar_licenca.sh
#
# Cria uma nova licença no banco, perguntando:
#   - a chave de licença (ou gera uma automaticamente)
#   - por quantos dias o acesso deve ficar válido
#
# Uso: ./criar_licenca.sh

set -euo pipefail

CONTAINER_POSTGRES="postgres-licencas"
DB_USER="appuser"
DB_NAME="licencas_db"

echo "=== Criar nova licença ==="
echo ""

# --- Chave de licença ---
read -rp "Chave de licença (deixe em branco para gerar automaticamente): " CHAVE

if [[ -z "$CHAVE" ]]; then
    CHAVE=$(cat /proc/sys/kernel/random/uuid | tr 'a-z' 'A-Z' | cut -c1-19)
    echo "Chave gerada automaticamente: $CHAVE"
fi

# --- Duração do acesso ---
read -rp "Por quantos dias o acesso deve ficar válido? (deixe em branco para acesso sem expiração): " DIAS

if [[ -z "$DIAS" ]]; then
    DATA_EXPIRACAO="NULL"
    echo "Acesso será criado SEM expiração (vitalício)."
else
    if ! [[ "$DIAS" =~ ^[0-9]+$ ]]; then
        echo "Erro: informe um número inteiro de dias."
        exit 1
    fi
    DATA_EXPIRACAO="NOW() + INTERVAL '${DIAS} days'"
    echo "Acesso será válido por $DIAS dias a partir de agora."
fi

# --- Confirmação antes de inserir ---
echo ""
echo "Resumo:"
echo "  Chave: $CHAVE"
echo "  Expira em: $([ "$DATA_EXPIRACAO" == "NULL" ] && echo "nunca" || echo "$DIAS dias")"
read -rp "Confirma a criação? (s/N): " CONFIRMA

if [[ ! "$CONFIRMA" =~ ^[sS]$ ]]; then
    echo "Cancelado."
    exit 0
fi

# --- Inserção no banco ---
docker exec -i "$CONTAINER_POSTGRES" psql -U "$DB_USER" -d "$DB_NAME" -c "
INSERT INTO licencas (chave_licenca, ativa, criada_em, data_expiracao)
VALUES ('$CHAVE', false, NOW(), $DATA_EXPIRACAO);
"

echo ""
echo "Licença criada com sucesso!"
echo "Chave: $CHAVE"
