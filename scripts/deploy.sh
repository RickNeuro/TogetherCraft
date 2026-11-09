#!/usr/bin/env bash
set -Eeuo pipefail
SOURCE_DIR="$(cd "$(dirname "$0")/.." && pwd)"
DEPLOY_DIR="${TOGETHERCRAFT_DEPLOY_DIR:-$HOME/apps/togethercraft/TogetherCraft}"
ENV_FILE="$DEPLOY_DIR/.env"
[[ -f "$ENV_FILE" ]] || { echo "Не найден production .env: $ENV_FILE" >&2; exit 1; }
for key in POSTGRES_PASSWORD APP_DB_PASSWORD ADMIN_PASSWORD; do
  grep -q "^$key=." "$ENV_FILE" || { echo "В production .env отсутствует $key." >&2; exit 1; }
done
docker info >/dev/null 2>&1 || { echo "Runner не имеет доступа к Docker." >&2; exit 1; }
COMPOSE=(docker compose --project-name togethercraft --env-file "$ENV_FILE" --file "$SOURCE_DIR/compose.yaml" --project-directory "$SOURCE_DIR")
NEW_VERSION="${GITHUB_SHA:-$(git -C "$SOURCE_DIR" rev-parse HEAD)}"
export APP_VERSION="${NEW_VERSION:0:12}"
wait_for_healthy() {
  local attempt status
  for attempt in $(seq 1 40); do
    status="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}unknown{{end}}' togethercraft-web 2>/dev/null || true)"
    [[ "$status" == healthy ]] && return 0
    sleep 2
  done
  docker logs --tail 100 togethercraft-web || true
  return 1
}
OLD_IMAGE_ID="$(docker inspect --format '{{.Image}}' togethercraft-web 2>/dev/null || true)"
echo "1/5: Проверяю PostgreSQL..."
"${COMPOSE[@]}" up -d postgres
"${COMPOSE[@]}" exec --no-TTY postgres pg_isready --username togethercraft --dbname togethercraft
BACKUP_DIR="$DEPLOY_DIR/backups"
mkdir -p "$BACKUP_DIR"; chmod 700 "$BACKUP_DIR"
BACKUP_FILE="$BACKUP_DIR/togethercraft-$(date -u +%Y%m%dT%H%M%SZ)-$APP_VERSION.dump"
echo "2/5: Создаю резервную копию PostgreSQL..."
"${COMPOSE[@]}" exec --no-TTY postgres pg_dump --username togethercraft --dbname togethercraft --format=custom > "$BACKUP_FILE"
chmod 600 "$BACKUP_FILE"
echo "3/5: Применяю bootstrap схемы..."
"${COMPOSE[@]}" run --rm db-bootstrap
echo "4/5: Собираю образ..."
"${COMPOSE[@]}" build togethercraft-web
echo "5/5: Запускаю новую версию..."
"${COMPOSE[@]}" up -d --no-deps --force-recreate togethercraft-web
if wait_for_healthy; then echo "Deploy completed: TogetherCraft $APP_VERSION is healthy."; exit 0; fi
echo "Health check failed; rolling back application image." >&2
if [[ -n "$OLD_IMAGE_ID" ]]; then
  docker tag "$OLD_IMAGE_ID" togethercraft-web:rollback
  export APP_VERSION=rollback
  "${COMPOSE[@]}" up -d --no-build --no-deps --force-recreate togethercraft-web
  wait_for_healthy || echo "Rollback health check failed; manual intervention required." >&2
else
  echo "No previous image available for rollback." >&2
fi
exit 1
