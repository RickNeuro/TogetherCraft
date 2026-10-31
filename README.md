# TogetherCraft

TogetherCraft — сайт для Minecraft-сервера на Fabric 26.2. Через сайт игрок может создать аккаунт и отправить заявку на вступление. Администратор рассматривает заявки и, если Minecraft RCON настроен, добавляет одобренный ник в whitelist.

## Что уже есть

- Регистрация и вход через аккаунты в PostgreSQL.
- Новые аккаунты получают статус ожидания.
- Страница администратора со списком заявок.
- Одобрение и отклонение заявок.
- Добавление одобренного Minecraft-ника в whitelist через RCON.
- Мониторинг Ubuntu через Prometheus и Node Exporter.
- Проверка состояния приложения по адресу `/healthz`.

Мониторинг доступен вошедшим пользователям со статусом Approved и администратору. На странице показываются CPU, оперативная память, системный диск и доступные аппаратные датчики. Температура и обороты вентиляторов зависят от того, какие датчики видит Linux. TPS, MSPT и список игроков Minecraft пока не подключены.

## Технологии

- C# и ASP.NET Core (.NET 8).
- PostgreSQL.
- Bootstrap, HTML, CSS и JavaScript.
- Docker Compose.
- Prometheus и Node Exporter для системных показателей.

## Запуск на Ubuntu

Открой папку проекта:

```bash
cd ~/apps/togethercraft/TogetherCraft
```

При первом запуске, когда файла `.env` ещё нет, можно использовать скрипт:

```bash
bash scripts/first-start.sh
```

Скрипт создаст `.env`, сгенерирует пароль администратора и запустит контейнеры. Сохрани пароль, который он покажет.

Если `.env` уже существует, не запускай скрипт повторно: он специально не перезаписывает настройки и пароли.

Для обычного запуска или обновления контейнеров:

```bash
sudo docker compose up -d
```

После изменения исходников пересобери веб-приложение:

```bash
sudo docker compose up -d --build togethercraft-web
```

Проверить состояние контейнеров:

```bash
sudo docker compose ps
```

Посмотреть журнал сайта:

```bash
sudo docker compose logs -f togethercraft-web
```

Проверить подключение к базе данных:

```bash
curl http://127.0.0.1:8080/healthz
```

## Запуск на Windows

Нужен Docker Desktop с включённым Linux-контейнерным режимом.

1. Распакуй проект и открой PowerShell в папке, где лежит `compose.yaml`.
2. Создай файл настроек:

   ```powershell
   Copy-Item .env.example .env
   ```

3. Открой `.env` и задай два разных надёжных пароля: `POSTGRES_PASSWORD` и `ADMIN_PASSWORD`. Значения `CHANGE_ME...` нужно заменить.
4. Запусти контейнеры:

   ```powershell
   docker compose up -d --build
   ```

5. Открой сайт: http://localhost:8080

Для входа администратора используй `ADMIN_USERNAME` и `ADMIN_PASSWORD` из файла `.env`.

## Где хранятся настройки и данные

- `.env` — пароли базы данных и администратора, настройки RCON и домена. Этот файл не нужно загружать в GitHub.
- `postgres-data` — Docker volume с данными аккаунтов.
- `monitoring/prometheus.yml` — настройки сбора системных показателей.
- `wwwroot` — изображения, CSS и JavaScript сайта.
- `Views` — Razor-страницы.
- `Controllers` — обработчики страниц и запросов.
- `Services` — работа с PostgreSQL, Minecraft RCON и Prometheus.

Не удаляй Docker volume `postgres-data`, если хочешь сохранить зарегистрированные аккаунты. Команда `docker compose down -v` удаляет volumes и может привести к потере данных.

## Настройка whitelist

Для автоматического добавления игроков нужен включённый RCON на Minecraft-сервере.

1. Найди `server.properties`, который использует работающий Fabric-сервер.
2. Настрой RCON и включи whitelist. Подробные параметры находятся в [docs/WHITELIST.md](docs/WHITELIST.md).
3. Укажи тот же RCON-пароль в `.env` сайта в параметре `RCON_PASSWORD`.
4. Перезапусти веб-контейнер, чтобы он прочитал новые настройки:

   ```bash
   sudo docker compose up -d --force-recreate togethercraft-web
   ```

Не открывай порт RCON (25575) в интернет и не загружай пароли в GitHub. Если RCON не настроен или Minecraft не отвечает, заявка останется ожидающей.

## Доступ из локальной сети и интернет

По умолчанию сайт доступен на порту 8080. На самом сервере его можно открыть по адресу http://127.0.0.1:8080. С другого устройства в той же локальной сети используй IP-адрес Ubuntu, например `http://192.168.0.61:8080`.

Для доступа из интернета нужно отдельно настроить домен и HTTPS. В проекте для этого предусмотрен профиль Caddy:

```bash
sudo docker compose --profile public up -d --build
```

Перед запуском укажи реальный домен в `PUBLIC_DOMAIN` файла `.env` и настрой DNS. Если провайдер не пропускает входящие подключения, понадобится туннель. Не публикуй в интернет PostgreSQL или RCON.

## Полезные команды Docker

```bash
sudo docker compose ps
sudo docker compose logs -f togethercraft-web
sudo docker compose logs -f postgres
sudo docker compose restart togethercraft-web
```

Чтобы остановить контейнеры без удаления базы данных:

```bash
sudo docker compose down
```
