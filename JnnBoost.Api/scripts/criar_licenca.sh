#!/bin/sh
#
# criar_licenca.sh
#
# Cria uma nova licença no banco, perguntando:
#   - a chave de licença (ou gera uma automaticamente)
#   - por quantos dias o acesso deve ficar válido
#
# Uso:
#   sh criar_licenca.sh
#   ou
#   ./criar_licenca.sh

set -eu

CONTAINER_POSTGRES="postgres-licencas"
DB_USER="appuser"
DB_NAME="licencas_db"

echo "=== Criar nova licença ==="
echo ""

# ----------------------------
# Chave da licença
# ----------------------------
printf "Chave de licença (deixe em branco para gerar automaticamente): "
read CHAVE

if [ -z "$CHAVE" ]; then
    CHAVE=$(cat /proc/sys/kernel/random/uuid | tr 'a-z' 'A-Z' | cut -c1-19)
    echo "Chave gerada automaticamente: $CHAVE"
fi

echo ""

# ----------------------------
# Dias de validade
# ----------------------------
printf "Por quantos dias o acesso deve ficar válido? (deixe em branco para acesso sem expiração): "
read DIAS

if [ -z "$DIAS" ]; then
    DATA_EXPIRACAO="NULL"
    EXPIRA="Nunca"
    echo "Acesso será criado SEM expiração."
else
    case "$DIAS" in
        ''|*[!0-9]*)
            echo "Erro: informe um número inteiro de dias."
            exit 1
            ;;
    esac

    DATA_EXPIRACAO="NOW() + INTERVAL '${DIAS} days'"
    EXPIRA="$DIAS dias"
    echo "Acesso será válido por $DIAS dias."
fi

echo ""
echo "========== Resumo =========="
echo "Chave........: $CHAVE"
echo "Expiração....: $EXPIRA"
echo "============================"
echo ""

printf "Confirma a criação? (s/N): "
read CONFIRMA

case "$CONFIRMA" in
    s|S)
        ;;
    *)
        echo "Operação cancelada."
        exit 0
        ;;
esac

docker exec -i "$CONTAINER_POSTGRES" psql -U "$DB_USER" -d "$DB_NAME" <<EOF
INSERT INTO licencas (
    chave_licenca,
    ativa,
    criada_em,
    data_expiracao,
    revogada
)
VALUES (
    '$CHAVE',
    false,
    NOW(),
    $DATA_EXPIRACAO,
    false
);
EOF

echo ""
echo "======================================"
echo "Licença criada com sucesso!"
echo ""
echo "Chave: $CHAVE"
echo "======================================"
