@echo off
chcp 65001 >nul
title Pure Accessory - Otomatik Guncelle ve Git'e Pushla

powershell -ExecutionPolicy Bypass -File "%~dp0Deploy_Version.ps1" %*
pause
