# TogetherCraft

TogetherCraft — сайт сообщества с двумя отдельными Minecraft-мирами: Fabric для совместного выживания и Create с техникой, поездами и автоматизацией.

## Что есть в проекте

- Главная страница в стиле TogetherCraft v4: тёмный графитовый фон, жёлтый акцент, исходный арт с лисой, FAQ и ссылки сообщества.
- Два игровых сервера с отдельными адресами и статусами:
  - Fabric — Minecraft 26.2, адрес fried-newer.tun.ply.gg.
  - Create — Minecraft 1.21.1, NeoForge 21.1.248, адрес shrimp-crafts.tun.ply.gg.
- Статус каждого игрового мира, версия, онлайн-игроки и время ответа запрашиваются через Minecraft Server List Ping. Проверки выполняются независимо и обновляются автоматически.
- Системные метрики Ubuntu: CPU, RAM, системный диск, температура CPU и каналы вентиляторов FAN1/FAN3 через Prometheus и Node Exporter.
- Регистрация, защищённая авторизация, заявки на доступ и административное одобрение в PostgreSQL.
- При одобрении сайт добавляет Minecraft-ник в whitelist Fabric и Create через два отдельных локальных RCON-подключения. Статус пользователя меняется на Approved только после подтверждения обоих серверов.
- Мониторинг доступен только одобренным аккаунтам. Команд управления игровыми серверами с сайта нет.

## Конфигурация серверов

Публичные адреса отображаются без портов. Внутренние адреса и порты для проверок задаются в .env:

- Fabric status check: MC_SERVER_HOST / MC_SERVER_PORT / MC_SERVER_PROTOCOL.
- Create status check: MC_CREATE_SERVER_HOST / MC_CREATE_SERVER_PORT / MC_CREATE_SERVER_PROTOCOL. По умолчанию это host.docker.internal:25566, то есть локальный порт Create на Ubuntu.
- Fabric RCON: RCON_HOST / RCON_PORT / RCON_PASSWORD.
- Create RCON: RCON_CREATE_HOST / RCON_CREATE_PORT / RCON_CREATE_PASSWORD.

У Create должен быть включён whitelist и отдельный RCON на порту 25576. Подробная инструкция находится в docs/WHITELIST.md. Не открывай RCON-порты наружу и никогда не коммить локальный .env.

## Запуск через Docker

Нужен Docker Engine с Docker Compose v2.

1. Скопируй .env.example в .env.
2. Замени секреты базы и администратора на длинные случайные значения.
3. Заполни оба RCON-пароля и выставь соответствующие значения в server.properties Fabric и Create.
4. Запусти контейнеры:

   bash scripts/first-start.sh

   Либо вручную:

   docker compose up --build -d

5. Открой http://127.0.0.1:8080 и войди под учётной записью администратора из .env.

После изменений кода можно пересобрать веб-контейнер:

   bash scripts/rebuild.sh

Скрипт не удаляет PostgreSQL volume. Не используй docker compose down -v, если не хочешь удалить данные базы.

## Полезные команды

   docker compose ps
   docker compose logs -f togethercraft-web
   docker compose logs -f postgres

PostgreSQL работает в отдельном контейнере и хранится в именованном volume. Порт базы не опубликован наружу. Prometheus и Node Exporter находятся во внутренней сети Docker.

## HTTPS

Для публичного HTTPS предусмотрен Caddy. Для его включения нужен домен и возможность принимать входящие соединения на 80/443 либо отдельно настроенный туннель. Укажи PUBLIC_DOMAIN в .env и выполни:

   docker compose --profile public up --build -d

Не публикуй наружу PostgreSQL или RCON.

## Совместимость и сборка

Проект использует .NET 8 и Npgsql 8.0.7. Код нужно собирать и запускать с установленным .NET SDK и Docker. Сборка и runtime-проверка должны выполняться перед выкладкой на рабочий сервер. В Visual Studio открой TogetherCraft.sln.
