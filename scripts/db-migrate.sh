#!/bin/sh
set -eu

: "${POSTGRES_PASSWORD:?POSTGRES_PASSWORD is required}"
: "${APP_DB_PASSWORD:?APP_DB_PASSWORD is required}"
: "${POSTGRES_DB:=togethercraft}"
: "${POSTGRES_USER:=togethercraft}"
: "${POSTGRES_HOST:=postgres}"

export PGPASSWORD="$POSTGRES_PASSWORD"

# Create/update the least-privilege application role before applying grants.
psql --host "$POSTGRES_HOST" --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
    --set=ON_ERROR_STOP=1 --set=app_db_password="$APP_DB_PASSWORD" <<'SQL'
SELECT format('CREATE ROLE tc_app LOGIN PASSWORD %L', :'app_db_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'tc_app')
\gexec

SELECT format('ALTER ROLE tc_app WITH LOGIN PASSWORD %L', :'app_db_password')
\gexec
SQL

for migration in /migrations/*.sql; do
    [ -f "$migration" ] || continue
    echo "Applying $(basename "$migration")..."
    psql --host "$POSTGRES_HOST" --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
        --set=ON_ERROR_STOP=1 --file "$migration"
done

echo "TogetherCraft database migrations completed."
