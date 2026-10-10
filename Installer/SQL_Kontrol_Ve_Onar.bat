@echo off
chcp 65001 >nul
title Pure Accessory - SQL Server Kontrol ve Onarım Sihirbazı

echo.
echo ========================================================
echo   SQL SERVER BAGLANTI KONTROL VE ONARIM BASLATILIYOR...
echo ========================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0SQL_Kontrol_Ve_Onar.ps1"
pause
