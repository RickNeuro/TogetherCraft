# TogetherCraft

TogetherCraft — сайт сообщества с двумя отдельными Minecraft-мирами: Fabric для совместного выживания и Create с техникой, поездами и автоматизацией.

## Что есть в проекте

- Главная страница в стиле TogetherCraft v4: тёмный графитовый фон, жёлтый акцент, FAQ и ссылки сообщества.
- Два игровых сервера с отдельными адресами и статусами:
  - Fabric — Minecraft 26.2, адрес fried-newer.tun.ply.gg.
  - Create — Minecraft 1.21.1, NeoForge 21.1.248, адрес shrimp-crafts.tun.ply.gg.
- Статус каждого игрового мира, версия, онлайн-игроки и время ответа запрашиваются через Minecraft Server List Ping.
- Системные метрики Ubuntu: CPU, RAM, диск, температура и вентиляторы через Prometheus и Node Exporter.
- Регистрация, авторизация, заявки на доступ и административное одобрение в PostgreSQL.
- При одобрении сайт добавляет Minecraft-ник в whitelist выбранных миров через RCON.

## Конфигурация

Публичные адреса отображаются на сайте. Внутренние адреса и порты для проверок задаются в `.env`:

- Fabric status check: `MC_SERVER_HOST`, `MC_SERVER_PORT`, `MC_SERVER_PROTOCOL`.
- Create status check: `MC_CREATE_SERVER_HOST`, `MC_CREATE_SERVER_PORT`, `MC_CREATE_SERVER_PROTOCOL`.
- Fabric RCON: `RCON_HOST`, `RCON_PORT`, `RCON_PASSWORD`.
- Create RCON: `RCON_CREATE_HOST`, `RCON_CREATE_PORT`, `RCON_CREATE_PASSWORD`.

Не открывай PostgreSQL и RCON наружу. Никогда не коммить локальный `.env`.

## Запуск через Docker

Нужен Docker Engine с Docker Compose v2.

Для новой установки:

1. Проверь `.env.example` и настройки игровых серверов.
2. Запусти:

   ```bash
   bash scripts/first-start.sh
   ```

3. Скрипт создаст разные случайные пароли для PostgreSQL, приложения и администратора, затем запустит Compose.
4. Открой `http://127.0.0.1:8080`.

Для ручного запуска сначала скопируй `.env.example` в `.env`, задай разные сильные значения `POSTGRES_PASSWORD`, `APP_DB_PASSWORD` и `ADMIN_PASSWORD`, затем выполни:

```bash
docker compose up --build -d
```

Миграции БД запускаются отдельным одноразовым сервисом до старта сайта. Они создают роль `tc_app` с ограниченными правами и применяют SQL-файлы из `db/migrations`.

**Обновление существующей установки:** если в старом `.env` ещё нет `APP_DB_PASSWORD`, один раз выполни:

```bash
bash scripts/ensure-app-db-password.sh
docker compose up --build -d
```

Скрипт добавит отдельный случайный пароль и не перезапишет остальные настройки. PostgreSQL volume и существующие записи не удаляются.

## CI/CD на GitHub

Workflow находится в `.github/workflows/ci-cd.yml`.

При каждом push в `feature/home-v4-two-servers`, `automation/ci-cd` или `main`, а также при Pull Request в `feature/home-v4-two-servers` и `main`, GitHub Actions выполняет:

1. Restore NuGet и Release-сборку .NET.
2. Проверку конфигурации Docker Compose.
3. Запуск отдельной временной PostgreSQL в GitHub Actions.
4. Создание роли `tc_app` и применение SQL-миграций.
5. Сборку и запуск приложения в Docker.
6. Проверку подключения backend к БД и прав пользователя `tc_app`.
7. HTTP-проверки главной, входа, регистрации, входа администратора и доступности локальных CSS/JavaScript-файлов.
8. Запуск Chromium через Playwright: проверяются главная страница, две игровые карточки, переход к серверам, формы входа/регистрации и отсутствие горизонтальной прокрутки на desktop/mobile ширине.

Browser tests используют Node.js 22, Playwright 1.65.0 и Chromium. При ошибке GitHub Actions сохраняет скриншоты и trace на 7 дней.

Временная база CI уничтожается после проверки. Production volumes эта задача не затрагивает. Интеграция с реальными Microsoft OAuth, RCON и игровыми серверами в этом CI-стенде не выполняется: для неё нужны отдельные тестовые учётные данные и доступные тестовые серверы.

### Автоматический деплой на Ubuntu

Деплой выполняется только после успешной задачи `verify`, только для push в `feature/home-v4-two-servers` и только после включения переменной `DEPLOY_ENABLED=true`. Pull Request и ветка `main` не выкладываются на production.

Для деплоя нужен self-hosted GitHub Actions Runner на Ubuntu-сервере с метками:

- `self-hosted`
- `linux`
- `x64`
- `togethercraft-deploy`

Runner сам опрашивает GitHub — входящий SSH-порт на роутере открывать не нужно. Запусти runner от отдельного Linux-пользователя, которому доступны production `.env` и Docker. Не запускай его от root. Участники, которым разрешено отправлять код в deploy-ветку, должны быть доверенными: доступ к Docker фактически даёт широкие права на хосте.

В GitHub открой `Settings → Secrets and variables → Actions → Variables` и добавь:

- `DEPLOY_ENABLED` = `true`.
- `DEPLOY_PATH` = полный путь к production-каталогу, где находится `.env`, например `/home/USERNAME/apps/togethercraft/TogetherCraft`. Если переменная не задана, используется `$HOME/apps/togethercraft/TogetherCraft` пользователя runner.

Runner должен иметь доступ к Docker без интерактивного sudo. Добавление пользователя в группу `docker` даёт ему практически root-доступ к серверу — используй отдельную учётную запись только для этой задачи.

Каждый deploy:

1. Проверяет наличие production `.env` и Docker.
2. Делает резервную копию PostgreSQL в `backups/` (права каталога 700, файла 600; локальное хранение 30 дней).
3. Применяет миграции.
4. Собирает образ с тегом commit SHA.
5. Пересоздаёт только веб-контейнер, не удаляя БД и остальные volumes.
6. Проверяет Docker health check. Если новый образ не становится healthy, скрипт пытается вернуть предыдущий образ приложения.

Откат приложения не откатывает SQL-схему. Перед миграциями поэтому создаётся backup; восстановление несовместимой миграции из backup остаётся отдельной административной операцией. Для защиты от отказа самого диска позже стоит добавить копирование backup на другой хост или в отдельное хранилище.

Пока runner и GitHub Variables не настроены, CI будет выполнять проверки, а deploy будет пропускаться.

## Полезные команды

```bash
docker compose ps
docker compose logs -f togethercraft-web
docker compose logs -f postgres
```

Не используй `docker compose down -v` на production: эта команда удалит именованные volumes проекта.

## HTTPS

Для публичного HTTPS предусмотрен Caddy. Нужен домен и возможность принимать входящие соединения на 80/443 либо отдельно настроенный туннель. Укажи `PUBLIC_DOMAIN` в `.env` и запусти:

```bash
docker compose --profile public up --build -d
```

## Совместимость

Проект использует .NET 8 и Npgsql 8.0.7. Для сборки нужен .NET SDK 8. В Visual Studio открой `TogetherCraft.sln`.
