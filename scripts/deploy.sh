#!/usr/bin/env bash
set -Eeuo pipefail

SOURCE_DIR="$(cd "$(dirname "$0")/.." && pwd)"
DEPLOY_DIR="${TOGETHERCRAFT_DEPLOY_DIR:-$HOME/apps/togethercraft/TogetherCraft}"
ENV_FILE="$DEPLOY_DIR/.env"

if [[ ! -f "$ENV_FILE" ]]; then
    echo "Не найден production .env: $ENV_FILE" >&2
    exit 1
fi
if ! grep -q '^APP_DB_PASSWORD=.' "$ENV_FILE"; then
    echo "В production .env отсутствует APP_DB_PASSWORD." >&2
    echo "Один раз запусти scripts/ensure-app-db-password.sh в каталоге проекта." >&2
    exit 1
fi
if ! docker info >/dev/null 2>&1; then
    echo "Пользователь GitHub Runner не может обращаться к Docker." >&2
    echo "Проверь группу docker и перезапусти службу runner." >&2
    exit 1
fi

COMPOSE=(docker compose --project-name togethercraft --env-file "$ENV_FILE" \
    --file "$SOURCE_DIR/compose.yaml" --project-directory "$SOURCE_DIR")
NEW_VERSION="${GITHUB_SHA:-$(git -C "$SOURCE_DIR" rev-parse HEAD)}"
NEW_VERSION="${NEW_VERSION:0:12}"
export APP_VERSION="$NEW_VERSION"

wait_for_healthy() {
    local attempt status
    for attempt in $(seq 1 35); do
        status="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}unknown{{end}}' togethercraft-web 2>/dev/null || true)"
        if [[ "$status" == "healthy" ]]; then
            return 0
        fi
        sleep 2
    done
    docker logs --tail 100 togethercraft-web || true
    return 1
}

OLD_IMAGE_ID="$(docker inspect --format '{{.Image}}' togethercraft-web 2>/dev/null || true)"

echo "1/5: Проверяю PostgreSQL..."
"${COMPOSE[@]}" up -d postgres

# Keep a private database snapshot before applying any schema changes.
BACKUP_DIR="$DEPLOY_DIR/backups"
mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"
BACKUP_FILE="$BACKUP_DIR/togethercraft-$(date -u +%Y%m%dT%H%M%SZ)-$NEW_VERSION.dump"
echo "2/5: Сохраняю резервную копию PostgreSQL..."
"${COMPOSE[@]}" exec --no-TTY postgres pg_dump --username togethercraft --dbname togethercraft --format=custom > "$BACKUP_FILE"
chmod 600 "$BACKUP_FILE"
find "$BACKUP_DIR" -maxdepth 1 -type f -name 'togethercraft-*.dump' -mtime +30 -delete

echo "3/5: Применяю миграции БД..."
"${COMPOSE[@]}" run --rm db-migrate

echo "4/5: Собираю образ $NEW_VERSION..."
"${COMPOSE[@]}" build togethercraft-web

echo "5/5: Запускаю новую версию..."
"${COMPOSE[@]}" up -d --no-deps --force-recreate togethercraft-web

if wait_for_healthy; then
    echo "Deploy completed: TogetherCraft $NEW_VERSION is healthy."
    exit 0
fi

echo "Новая версия не прошла health check. Выполняю откат..." >&2
if [[ -n "$OLD_IMAGE_ID" ]]; then
    docker tag "$OLD_IMAGE_ID" togethercraft-web:rollback
    export APP_VERSION=rollback
    "${COMPOSE[@]}" up -d --no-build --no-deps --force-recreate togethercraft-web
    if wait_for_healthy; then
        echo "Откат приложения выполнен: предыдущая версия восстановлена." >&2
    else
        echo "ОШИБКА: предыдущая версия тоже не прошла health check. Нужна ручная проверка." >&2
    fi
else
    echo "Предыдущего образа нет — автоматический откат невозможен." >&2
fi
exit 1
