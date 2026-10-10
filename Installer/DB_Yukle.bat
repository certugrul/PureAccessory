@echo off
chcp 65001 >nul
title Pure Accessory ERP - Veritabani Yukleme
cd /d "%~dp0"

echo ==========================================================
echo     PURE ACCESSORY ERP - VERITABANI YUKLEME (DB)
echo ==========================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Restore_Database.ps1"

pause
