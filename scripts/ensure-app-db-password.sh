#!/usr/bin/env bash
set -Eeuo pipefail

cd "$(dirname "$0")/.."

if [[ ! -f .env ]]; then
    echo "Файл .env не найден. Сначала настрой окружение TogetherCraft."
    exit 1
fi

if grep -q '^APP_DB_PASSWORD=' .env; then
    current_value="$(sed -n 's/^APP_DB_PASSWORD=//p' .env | head -n 1)"
    if [[ -n "$current_value" ]]; then
        echo "APP_DB_PASSWORD уже настроен. Файл .env не менялся."
        exit 0
    fi
    sed -i '/^APP_DB_PASSWORD=/d' .env
fi

if ! command -v openssl >/dev/null 2>&1; then
    echo "Не найден openssl. Установи его: sudo apt install openssl"
    exit 1
fi

app_db_password="$(openssl rand -hex 32)"
printf '\nAPP_DB_PASSWORD=%s\n' "$app_db_password" >> .env
chmod 600 .env
unset app_db_password

echo "Создан отдельный пароль tc_app в .env. Сам пароль не выводился."
echo "Теперь можно запускать миграцию БД и TogetherCraft."
