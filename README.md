# TogetherCraft

Сайт сообщества с двумя Minecraft-мирами: Fabric для совместного выживания и Create с техникой, поездами и автоматизацией.

## Возможности

- Главная страница, два игровых мира, FAQ и ссылки сообщества.
- Публичный read-only статус Fabric и Create: доступность, версия, игроки и отклик.
- Системные метрики Ubuntu (CPU, RAM, диск, температура, FAN1/FAN3) только для одобренных аккаунтов.
- Регистрация, вход, подтверждение Minecraft Java через Microsoft и административное одобрение.
- Автоматический whitelist выбранных игровых миров через два отдельных RCON.
- PostgreSQL, Prometheus и Node Exporter. Сайт не отправляет команды управления игровыми серверами.

## Требования

- Docker Engine и Docker Compose v2.
- OpenSSL для генерации секретов.
- Для локальной сборки: .NET 8 SDK; для Visual Studio — Visual Studio 2022 17.8 или новее.

## Первый запуск

Из корня репозитория запусти:

~~~bash
bash scripts/first-start.sh
~~~

Скрипт создаст .env из .env.example, сгенерирует уникальные секреты для PostgreSQL, роли приложения и администратора, выставит права файла 600 и запустит контейнеры.

При старте Compose сначала ждёт PostgreSQL, затем отдельный db-bootstrap:
1. создаёт роль tc_app;
2. создаёт исходную таблицу tc_users, если её ещё нет;
3. применяет db/migrations/002-registration-worlds.sql;
4. выдаёт приложению необходимые права на таблицу и последовательность.

Bootstrap повторяемый и не удаляет существующие данные. При повторном запуске first-start.sh существующие пароли PostgreSQL и администратора не заменяются. Если в старом .env отсутствует APP_DB_PASSWORD, скрипт добавит его, после чего bootstrap синхронизирует пароль роли tc_app. Если в существующем .env нет действующего POSTGRES_PASSWORD или ADMIN_PASSWORD, скрипт остановится и попросит указать правильные значения.

При первом запуске сгенерированный пароль администратора выводится в терминал один раз. Сохрани его. Не публикуй .env и не отправляй его в GitHub.

## Microsoft и регистрация

Для подтверждения Minecraft Java Edition создай приложение Microsoft для личных аккаунтов и добавь callback URI вида:

~~~text
https://YOUR_DOMAIN/signin-microsoft-minecraft
~~~

Укажи MICROSOFT_CLIENT_ID и MICROSOFT_CLIENT_SECRET в локальном .env. Без них регистрация с подтверждением профиля не завершится; главная страница, публичный статус игровых серверов и вход существующих пользователей остаются доступны.

После изменения .env пересоздай веб-контейнер:

~~~bash
bash scripts/rebuild.sh
~~~

## Игровые серверы и RCON

Настройки проверок Minecraft находятся в .env:

- Fabric: MC_SERVER_HOST, MC_SERVER_PORT, MC_SERVER_PROTOCOL, MC_SERVER_DISPLAY_ADDRESS.
- Create: MC_CREATE_SERVER_HOST, MC_CREATE_SERVER_PORT, MC_CREATE_SERVER_PROTOCOL, MC_CREATE_SERVER_DISPLAY_ADDRESS.

Whitelist настраивается отдельно для двух серверов:

- Fabric: RCON_HOST, RCON_PORT, RCON_PASSWORD.
- Create: RCON_CREATE_HOST, RCON_CREATE_PORT, RCON_CREATE_PASSWORD.

В server.properties обоих серверов включи enable-rcon=true и white-list=true. Используй разные уникальные пароли и порты (Fabric 25575, Create 25576 по умолчанию). Не открывай RCON в интернет.

Подробная инструкция: docs/WHITELIST.md.

## Обновление

После переключения на обновлённую ветку запусти:

~~~bash
bash scripts/rebuild.sh
~~~

Скрипт сначала проверит конфигурацию, применит bootstrap базы, затем пересоберёт веб-контейнер. Именованный volume PostgreSQL не удаляется.

Полезные команды:

~~~bash
docker compose ps
docker compose logs -f togethercraft-web
docker compose logs -f postgres
docker compose logs -f db-bootstrap
~~~

Не используй docker compose down -v для обычного обновления: эта команда удаляет именованные тома, в том числе базу и историю Prometheus.

## HTTPS

Публичный HTTPS предусмотрен через Caddy. Нужен домен и доступность портов 80/443 либо правильно настроенный туннель.

Укажи реальный PUBLIC_DOMAIN в .env и запусти:

~~~bash
docker compose --profile public up --build -d
~~~

Caddy закреплён за IP 172.30.0.10. Приложение принимает X-Forwarded-For и X-Forwarded-Proto только от этого прокси. Если подсеть 172.30.0.0/24 уже занята в Docker, измени сеть и IP согласованно в compose.yaml и ForwardedHeaders:KnownProxy.

Не публикуй PostgreSQL, Prometheus, Node Exporter или RCON наружу.

## Проверки

GitHub Actions выполняет restore и Release-сборку .NET, проверку Docker Compose, запуск временной PostgreSQL, проверку прав tc_app, smoke-проверки API/авторизации/локальных ресурсов и браузерные проверки Chromium на ширинах 360, 390 и 1440 px. В этом CI нет выкладки на рабочий сервер.

## Версии

Приложение остаётся на .NET 8 ради совместимости с текущей средой Visual Studio 2022. Поддержка .NET 8 заканчивается 10 ноября 2026 года; переход на .NET 10 требует обновления SDK и среды разработки одновременно. Сейчас используется Npgsql 8.0.9.
