@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\stop-preview.ps1"
if errorlevel 1 (
    echo.
    echo Не удалось остановить предпросмотр. Прочитай сообщение об ошибке выше.
    pause
)
