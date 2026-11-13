$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $ProjectRoot

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker не найден."
}

$ComposePrefix = @("--project-name", "togethercraft-preview", "--file", "compose.yaml")
& docker compose @ComposePrefix down
if ($LASTEXITCODE -ne 0) {
    throw "Не удалось остановить локальный предпросмотр."
}

Write-Host "Предпросмотр остановлен. Локальная база сохранена." -ForegroundColor Green
Write-Host "Для следующего запуска снова выполни .\scripts\start-preview.ps1"
