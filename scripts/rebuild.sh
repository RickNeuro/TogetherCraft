#!/usr/bin/env bash
set -Eeuo pipefail
cd "$(dirname "$0")/.."

DOCKER=(docker)
if ! docker info >/dev/null 2>&1; then
    if command -v sudo >/dev/null 2>&1 && sudo docker info >/dev/null 2>&1; then
        DOCKER=(sudo docker)
    else
        echo "Docker daemon недоступен. Проверь systemctl status docker и права пользователя."
        exit 1
    fi
fi

"${DOCKER[@]}" compose config --quiet
"${DOCKER[@]}" compose run --rm db-bootstrap
"${DOCKER[@]}" compose up -d --build --force-recreate togethercraft-web
"${DOCKER[@]}" compose ps
echo "PostgreSQL volume не удалялся."
