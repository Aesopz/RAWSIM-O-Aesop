@echo off
title thesis watch
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0watch.ps1" %*
pause
