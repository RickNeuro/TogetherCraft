# Настройка автоматического whitelist

Админ-панель добавляет Minecraft-ник в whitelist **обоих игровых миров** — Fabric и Create — только после нажатия «Одобрить и добавить в whitelist». Браузер не обращается к RCON напрямую; пароли хранятся только в локальном .env на сервере приложения.

## 1. Fabric

Найди server.properties, который использует работающий Fabric. Для него должны быть включены:

~~~properties
white-list=true
enable-rcon=true
rcon.port=25575
rcon.password=УНИКАЛЬНЫЙ_СЕКРЕТ_FABRIC
broadcast-rcon-to-ops=false
~~~

Задай тот же пароль в .env приложения:

~~~dotenv
RCON_HOST=host.docker.internal
RCON_PORT=25575
RCON_PASSWORD=ТАКОЙ_ЖЕ_СЕКРЕТ_FABRIC
~~~

## 2. Create / NeoForge

Используй файл ~/servers/CreateServer/server.properties. Для Create нужен отдельный RCON-порт, не совпадающий с Fabric:

~~~properties
white-list=true
enable-rcon=true
rcon.port=25576
rcon.password=ДРУГОЙ_УНИКАЛЬНЫЙ_СЕКРЕТ_CREATE
broadcast-rcon-to-ops=false
~~~

Задай тот же Create-пароль в .env приложения:

~~~dotenv
RCON_CREATE_HOST=host.docker.internal
RCON_CREATE_PORT=25576
RCON_CREATE_PASSWORD=ТАКОЙ_ЖЕ_СЕКРЕТ_CREATE
~~~

Не используй один и тот же пароль для двух RCON. Перезапусти соответствующий Minecraft-сервер после изменения его server.properties.

## 3. Не открывай RCON в интернет

**Не настраивай проброс TCP 25575 или 25576 на роутере.** RCON должен быть доступен только с доверенной машины/локальной Docker-сети. Если на Ubuntu включён UFW или другой firewall, проверь правила, чтобы эти порты нельзя было достичь с внешнего интерфейса. Не отключай firewall целиком ради подключения.

В Compose host.docker.internal сопоставляется с адресом Docker-хоста. Оба игровых сервера должны слушать нужные RCON-порты на адресе, доступном Docker-хосту. Если контейнер не может подключиться, сначала проверь прослушиваемый адрес и firewall, а затем настрой только минимально необходимое разрешение.

## 4. Как работает одобрение заявки

1. Админ нажимает кнопку одобрения.
2. Серверная часть добавляет ник в whitelist Fabric.
3. Затем добавляет тот же ник в whitelist Create.
4. Только после подтверждения обоих серверов статус пользователя меняется на Approved.

Если Fabric подтвердил добавление, а Create временно недоступен, заявка остаётся Pending. Её можно безопасно повторить: Minecraft сообщает, что уже добавленный ник уже в whitelist, и повторная команда считается успешной.

После изменения .env пересоздай веб-контейнер:

~~~bash
docker compose up -d --force-recreate togethercraft-web
~~~

Проверь whitelist на обоих игровых серверах командой whitelist list.