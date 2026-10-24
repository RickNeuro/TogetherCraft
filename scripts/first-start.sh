#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

if ! command -v docker >/dev/null 2>&1; then
    echo "Docker не найден. Сначала установи Docker Engine и плагин Docker Compose."
    exit 1
fi
if ! docker compose version >/dev/null 2>&1; then
    echo "Не найден Docker Compose v2. Установи docker-compose-plugin."
    exit 1
fi
DOCKER=(docker)
if ! docker info >/dev/null 2>&1; then
    if command -v sudo >/dev/null 2>&1 && sudo docker info >/dev/null 2>&1; then
        DOCKER=(sudo docker)
    else
        echo "Docker daemon недоступен. Проверь systemctl status docker и права пользователя."
        exit 1
    fi
fi
if ! command -v openssl >/dev/null 2>&1; then
    echo "Не найден openssl. Установи его командой: sudo apt install openssl"
    exit 1
fi

if [[ -e .env ]]; then
    echo "Файл .env уже существует — не перезаписываю его и не трогаю текущие секреты."
    echo "Проверь его вручную и запусти: docker compose up --build -d"
    exit 1
fi

POSTGRES_PASSWORD="$(openssl rand -hex 32)"
ADMIN_PASSWORD="$(openssl rand -hex 32)"
cat > .env <<ENV
POSTGRES_PASSWORD=${POSTGRES_PASSWORD}
ADMIN_USERNAME=admin
ADMIN_PASSWORD=${ADMIN_PASSWORD}
RCON_HOST=host.docker.internal
RCON_PORT=25575
RCON_PASSWORD=
PUBLIC_DOMAIN=CHANGE_ME.example.com
ENV
chmod 600 .env

echo "Создан .env с уникальными секретами и правами 600."
echo "Логин администратора: admin"
echo "Временный пароль администратора: ${ADMIN_PASSWORD}"
echo "Сохрани этот пароль. Не отправляй .env в GitHub и не публикуй пароль."
echo
"${DOCKER[@]}" compose up --build -d
echo
echo "Состояние контейнеров:"
"${DOCKER[@]}" compose ps
echo
echo "Локальный адрес: http://127.0.0.1:8080"
echo "Whitelist пока не подключён: включи RCON по инструкции docs/WHITELIST.md, затем добавь RCON_PASSWORD в .env."
echo "Публичный HTTPS не включён: он требует домена и настройки доступности портов/туннеля."
