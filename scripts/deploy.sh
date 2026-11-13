#!/usr/bin/env bash
set -Eeuo pipefail

DEPLOY_DIR="${TOGETHERCRAFT_DEPLOY_DIR:-$HOME/apps/togethercraft/TogetherCraft}"
ENV_FILE="$DEPLOY_DIR/.env"
NEW_VERSION="${GITHUB_SHA:-}"

[[ -d "$DEPLOY_DIR/.git" ]] || { echo "Каталог не является Git-репозиторием: $DEPLOY_DIR" >&2; exit 1; }
[[ -f "$ENV_FILE" ]] || { echo "Не найден production .env: $ENV_FILE" >&2; exit 1; }

for key in POSTGRES_PASSWORD APP_DB_PASSWORD; do
    grep -q "^$key=." "$ENV_FILE" || { echo "В production .env отсутствует $key." >&2; exit 1; }
done

docker info >/dev/null 2>&1 || { echo "Runner не имеет доступа к Docker." >&2; exit 1; }

if [[ -z "$NEW_VERSION" ]]; then
    NEW_VERSION="$(git -C "$DEPLOY_DIR" rev-parse HEAD)"
fi
APP_VERSION="${NEW_VERSION:0:12}"
export APP_VERSION

echo "1/6: Проверяю рабочую копию на Ubuntu..."
if [[ -n "$(git -C "$DEPLOY_DIR" status --porcelain --untracked-files=no)" ]]; then
    echo "В каталоге есть локальные изменения отслеживаемых файлов. Отменяю deploy, чтобы ничего не перезаписать." >&2
    git -C "$DEPLOY_DIR" status --short >&2
    exit 1
fi

git -C "$DEPLOY_DIR" fetch --no-tags origin "$NEW_VERSION"
git -C "$DEPLOY_DIR" checkout --detach "$NEW_VERSION"
SOURCE_DIR="$DEPLOY_DIR"

COMPOSE=(docker compose --project-name togethercraft --env-file "$ENV_FILE" --file "$SOURCE_DIR/compose.yaml" --project-directory "$SOURCE_DIR")
"${COMPOSE[@]}" config --quiet

wait_for_postgres() {
    local attempt
    for attempt in $(seq 1 40); do
        if docker exec togethercraft-postgres pg_isready --username togethercraft --dbname togethercraft >/dev/null 2>&1; then
            return 0
        fi
        sleep 2
    done
    docker logs --tail 100 togethercraft-postgres || true
    return 1
}

echo "2/6: Восстанавливаю PostgreSQL без пересоздания Docker-сети..."
if [[ "$(docker inspect --format '{{.State.Running}}' togethercraft-postgres 2>/dev/null || true)" != "true" ]]; then
    docker start togethercraft-postgres
fi
wait_for_postgres || { echo "PostgreSQL не перешла в ready state." >&2; exit 1; }

OLD_IMAGE_ID="$(docker inspect --format '{{.Image}}' togethercraft-web 2>/dev/null || true)"

BACKUP_DIR="$DEPLOY_DIR/backups"
mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"
BACKUP_FILE="$BACKUP_DIR/togethercraft-$(date -u +%Y%m%dT%H%M%SZ)-$APP_VERSION.dump"

echo "3/6: Создаю резервную копию PostgreSQL..."
docker exec togethercraft-postgres pg_dump --username togethercraft --dbname togethercraft --format=custom > "$BACKUP_FILE"
chmod 600 "$BACKUP_FILE"
[[ -s "$BACKUP_FILE" ]] || { echo "Резервная копия пустая; миграции отменены." >&2; exit 1; }
echo "Backup saved: $BACKUP_FILE"

echo "4/6: Применяю повторяемый bootstrap схемы..."
"${COMPOSE[@]}" run --rm db-bootstrap

echo "5/6: Собираю версионированный образ..."
"${COMPOSE[@]}" build togethercraft-web

wait_for_healthy() {
    local attempt status
    for attempt in $(seq 1 40); do
        status="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}unknown{{end}}' togethercraft-web 2>/dev/null || true)"
        [[ "$status" == "healthy" ]] && return 0
        sleep 2
    done
    docker logs --tail 100 togethercraft-web || true
    return 1
}

echo "6/6: Запускаю новую версию..."
"${COMPOSE[@]}" up -d --no-deps --force-recreate togethercraft-web

if wait_for_healthy; then
    echo "Deploy completed: TogetherCraft $APP_VERSION is healthy."
    exit 0
fi

echo "Health check failed; attempting application-only rollback." >&2
if [[ -n "$OLD_IMAGE_ID" ]]; then
    docker tag "$OLD_IMAGE_ID" togethercraft-web:rollback
    export APP_VERSION=rollback
    "${COMPOSE[@]}" up -d --no-build --no-deps --force-recreate togethercraft-web
    wait_for_healthy || echo "Rollback health check failed; manual intervention required." >&2
else
    echo "No previous application image is available for rollback." >&2
fi
exit 1
