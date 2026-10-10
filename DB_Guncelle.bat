@echo off
chcp 65001 >nul
title Pure Accessory - Veritabanı Güncelleme ve Onarım

echo.
echo ==========================================================
echo    PURE ACCESSORY - VERİTABANI GÜNCELLEME SİHİRBAZI      
echo ==========================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0DB_Guncelle.ps1" %*

echo.
echo İşlem bitti. Çıkmak için bir tuşa basın...
pause >nul
