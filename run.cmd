@echo off
REM Start the whole app: API (:5002) + web (:5173, browser opens automatically), together.
REM Ctrl+C stops both. Usage from the repo root:  .\run
cd /d "%~dp0TaskFlow.Web"
call npm run dev:all
