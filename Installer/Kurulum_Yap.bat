@echo off
chcp 65001 >nul
title Pure Accessory Otomasyon - Kurulum
echo ========================================================
echo Pure Accessory Otomasyon Kurulumu Başlatılıyor...
echo ========================================================
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install_PureAccessory.ps1"
