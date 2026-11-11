$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $ProjectRoot

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker не найден. Установи Docker Desktop и повтори запуск."
}

$dockerVersion = & docker info --format '{{.ServerVersion}}' 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "Docker Engine не запущен. Открой Docker Desktop, дождись статуса Running и запусти скрипт снова."
}

$ComposePrefix = @("--project-name", "togethercraft-preview", "--file", "compose.yaml")
$envPath = Join-Path $ProjectRoot ".env"
$credentialsPath = Join-Path $ProjectRoot ".preview-admin.txt"

function New-SecureHex {
    $bytes = New-Object byte[] 32
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }
    return ([BitConverter]::ToString($bytes) -replace "-", "").ToLowerInvariant()
}

if (-not (Test-Path $envPath)) {
    if (-not (Test-Path (Join-Path $ProjectRoot ".env.example"))) {
        throw "Не найден .env.example. Распакуй архив полностью и запускай скрипт из папки TogetherCraft."
    }

    if (Test-Path $credentialsPath) {
        throw "Найден старый .preview-admin.txt, но .env отсутствует. Не буду создавать новую базу с другими реквизитами. Проверь или восстанови локальный .env."
    }

    $postgresPassword = New-SecureHex
    $appPassword = New-SecureHex
    $adminPassword = New-SecureHex

    $envText = [System.IO.File]::ReadAllText((Join-Path $ProjectRoot ".env.example"))
    $envText = [regex]::Replace($envText, "(?m)^POSTGRES_PASSWORD=.*$", "POSTGRES_PASSWORD=$postgresPassword")
    $envText = [regex]::Replace($envText, "(?m)^APP_DB_PASSWORD=.*$", "APP_DB_PASSWORD=$appPassword")
    $envText = [regex]::Replace($envText, "(?m)^ADMIN_USERNAME=.*$", "ADMIN_USERNAME=preview-admin")
    $envText = [regex]::Replace($envText, "(?m)^ADMIN_PASSWORD=.*$", "ADMIN_PASSWORD=$adminPassword")

    # Prevent the local preview from probing real TogetherCraft game servers.
    $envText = [regex]::Replace($envText, "(?m)^MC_SERVER_HOST=.*$", "MC_SERVER_HOST=127.0.0.1")
    $envText = [regex]::Replace($envText, "(?m)^MC_SERVER_PORT=.*$", "MC_SERVER_PORT=25565")
    $envText = [regex]::Replace($envText, "(?m)^MC_SERVER_DISPLAY_ADDRESS=.*$", "MC_SERVER_DISPLAY_ADDRESS=disabled-local-preview")
    $envText = [regex]::Replace($envText, "(?m)^MC_CREATE_SERVER_HOST=.*$", "MC_CREATE_SERVER_HOST=127.0.0.1")
    $envText = [regex]::Replace($envText, "(?m)^MC_CREATE_SERVER_PORT=.*$", "MC_CREATE_SERVER_PORT=25566")
    $envText = [regex]::Replace($envText, "(?m)^MC_CREATE_SERVER_DISPLAY_ADDRESS=.*$", "MC_CREATE_SERVER_DISPLAY_ADDRESS=disabled-local-preview")
    $envText = [regex]::Replace($envText, "(?m)^RCON_HOST=.*$", "RCON_HOST=127.0.0.1")
    $envText = [regex]::Replace($envText, "(?m)^RCON_CREATE_HOST=.*$", "RCON_CREATE_HOST=127.0.0.1")

    $envText += @"

# Local preview only: unique names, localhost binding and non-production ports.
WEB_BIND_ADDRESS=127.0.0.1
WEB_BIND_PORT=18080
POSTGRES_CONTAINER_NAME=togethercraft-preview-postgres
WEB_CONTAINER_NAME=togethercraft-preview-web
NODE_EXPORTER_CONTAINER_NAME=togethercraft-preview-node-exporter
PROMETHEUS_CONTAINER_NAME=togethercraft-preview-prometheus
CADDY_CONTAINER_NAME=togethercraft-preview-caddy
NODE_EXPORTER_BIND_ADDRESS=127.0.0.1
NODE_EXPORTER_BIND_PORT=19100
PROMETHEUS_BIND_ADDRESS=127.0.0.1
PROMETHEUS_BIND_PORT=19090
"@

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($envPath, $envText, $utf8NoBom)

    $credentials = @"
TogetherCraft — локальный предпросмотр
Адрес: http://localhost:18080
Вход: http://localhost:18080/Account/Login
Логин: preview-admin
Пароль: $adminPassword

Это отдельная локальная учётная запись. Она не связана с рабочим сайтом.
"@
    [System.IO.File]::WriteAllText($credentialsPath, $credentials, $utf8NoBom)
    Write-Host "Создана отдельная локальная база и временный администратор." -ForegroundColor Green
}
elseif (-not (Test-Path $credentialsPath)) {
    throw "Найден .env, но отсутствует .preview-admin.txt. Чтобы не менять существующие данные и пароль, автоматический запуск остановлен."
}

Write-Host "Проверяю конфигурацию Docker Compose..." -ForegroundColor Cyan
& docker compose @ComposePrefix config --quiet
if ($LASTEXITCODE -ne 0) {
    throw "Конфигурация Docker Compose содержит ошибку. Проверь файл .env."
}

Write-Host "Собираю и запускаю локальный TogetherCraft..." -ForegroundColor Cyan
& docker compose @ComposePrefix up --build --detach
if ($LASTEXITCODE -ne 0) {
    & docker compose @ComposePrefix logs --tail 80
    throw "Не удалось запустить предпросмотр. Последние логи выведены выше."
}

$healthUrl = "http://localhost:18080/healthz"
$ready = $false
for ($attempt = 1; $attempt -le 60; $attempt++) {
    try {
        $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 4
        if ($response.StatusCode -eq 200) {
            $ready = $true
            break
        }
    }
    catch {
        Start-Sleep -Seconds 2
    }
}

if (-not $ready) {
    & docker compose @ComposePrefix ps
    & docker compose @ComposePrefix logs --tail 80 togethercraft-web postgres db-bootstrap
    throw "Сайт не ответил на проверку готовности. Логи выведены выше."
}

Write-Host ""
Write-Host "TogetherCraft запущен локально." -ForegroundColor Green
Write-Host "Сайт:      http://localhost:18080"
Write-Host "Вход:      http://localhost:18080/Account/Login"
Write-Host "Реквизиты: .preview-admin.txt"
Write-Host "Остановка: .\scripts\stop-preview.ps1"
Write-Host "Игровые серверы и Microsoft OAuth в предпросмотре отключены." -ForegroundColor Yellow
Start-Process "http://localhost:18080"
