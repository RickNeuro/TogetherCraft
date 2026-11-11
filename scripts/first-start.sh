#!/usr/bin/env bash
set -Eeuo pipefail
cd "$(dirname "$0")/.."

if ! command -v docker >/dev/null 2>&1 || ! docker compose version >/dev/null 2>&1; then
    echo "Нужны Docker Engine и Docker Compose v2."
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
    echo "Не найден openssl. Установи: sudo apt install openssl"
    exit 1
fi

created_env=0
if [[ ! -f .env ]]; then
    cp .env.example .env
    created_env=1
fi

replace_env_value() {
    local key="$1"
    local value="$2"
    if grep -q "^${key}=" .env; then
        sed -i "/^${key}=/d" .env
    fi
    printf '%s=%s\n' "$key" "$value" >> .env
}

is_placeholder() {
    local key="$1"
    local value
    value="$(grep -E "^${key}=" .env | tail -n 1 | cut -d= -f2- || true)"
    case "$value" in
        ""|CHANGE_ME*|change_me*|REPLACE*|replace*|CHANGEME*|changeme*|example*)
            return 0 ;;
        *)
            return 1 ;;
    esac
}

ensure_secret() {
    local key="$1"
    if is_placeholder "$key"; then
        replace_env_value "$key" "$(openssl rand -hex 32)"
        return 0
    fi
    return 1
}

if [[ "$created_env" -eq 1 ]]; then
    ensure_secret POSTGRES_PASSWORD || true
    ensure_secret APP_DB_PASSWORD || true
else
    if is_placeholder POSTGRES_PASSWORD; then
        echo "В существующем .env нет рабочего POSTGRES_PASSWORD. Укажи пароль текущей базы вручную."
        exit 1
    fi
    ensure_secret APP_DB_PASSWORD || true
fi

chmod 600 .env
admin_username="$(grep -E '^ADMIN_USERNAME=' .env | tail -n 1 | cut -d= -f2-)"
admin_password="$(grep -E '^ADMIN_PASSWORD=' .env | tail -n 1 | cut -d= -f2-)"
echo "Проверяю конфигурацию Docker Compose..."
"${DOCKER[@]}" compose config --quiet

echo "Запускаю PostgreSQL и применяю миграции базы..."
"${DOCKER[@]}" compose up -d postgres
"${DOCKER[@]}" compose run --rm db-bootstrap

echo "Запускаю TogetherCraft..."
"${DOCKER[@]}" compose up --build -d

echo
"${DOCKER[@]}" compose ps
echo
echo "Локальный адрес: http://127.0.0.1:8080"
if [[ -n "$admin_username" ]]; then
    echo "Логин bootstrap: $admin_username"
    if [[ -z "$admin_password" ]]; then
        echo "Если такой аккаунт уже есть, он будет назначен Admin с прежним паролем."
        echo "Если аккаунта нет, задай сильный ADMIN_PASSWORD в .env (не менее 14 символов) и перезапусти."
    else
        echo "Если аккаунт уже существует, будет сохранён его прежний пароль; иначе он будет создан с ADMIN_PASSWORD."
    fi
    echo "После проверки очисти ADMIN_USERNAME в .env, чтобы bootstrap-настройка не влияла на дальнейшие изменения ролей."
else
    echo "Отдельная учётная запись admin не создаётся: ADMIN_USERNAME пуст."
fi
echo
echo "Для регистрации заполни MICROSOFT_CLIENT_ID и MICROSOFT_CLIENT_SECRET."
echo "Для whitelist настрой оба RCON-подключения в .env."
echo "Публичный HTTPS через Caddy настраивается отдельно. См. README.md."
