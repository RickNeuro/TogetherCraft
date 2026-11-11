@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\start-preview.ps1"
if errorlevel 1 (
    echo.
    echo Не удалось запустить предпросмотр. Прочитай сообщение об ошибке выше.
    pause
)
