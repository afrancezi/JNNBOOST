#!/usr/bin/env bash
#
# tentativas_bloqueadas.sh
#
# Mostra quais licenças sofreram tentativa de acesso por HWID diferente
# do cadastrado (indício de compartilhamento de chave), com contagem
# e data da última tentativa.
#
# Uso: ./tentativas_bloqueadas.sh

set -euo pipefail

CONTAINER_POSTGRES="postgres-licencas"
DB_USER="appuser"
DB_NAME="licencas_db"

echo "=== Licenças com tentativas bloqueadas ==="
echo ""

docker exec -i "$CONTAINER_POSTGRES" psql -U "$DB_USER" -d "$DB_NAME" -c "
SELECT
    l.chave_licenca,
    COUNT(t.id) AS total_tentativas,
    MAX(t.tentativa_em) AS ultima_tentativa
FROM tentativas_bloqueadas t
JOIN licencas l ON l.id = t.licenca_id
GROUP BY l.chave_licenca
ORDER BY total_tentativas DESC;
"