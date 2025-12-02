@echo off
echo Pin - SuperAPP başlatılıyor...
cd /d "%~dp0bin\Release\net9.0-windows\win-x64\publish"
start WebScraper.exe