@echo off
rem Downloads the offline translator model for ROSE Lingo (~1 GB, one time).
rem Run it where huggingface.co is reachable; then copy data\models to any offline laptop.
rem Extra arguments are passed on, e.g.  setup-lingo.cmd -Destination D:\models\nllb-200-distilled-600M
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-lingo.ps1" %*
set "RC=%ERRORLEVEL%"
if not defined ROSE_NO_PAUSE pause
exit /b %RC%