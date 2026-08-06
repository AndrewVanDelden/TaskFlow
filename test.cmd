@echo off
REM Run the full backend + frontend suites WITH coverage into test-results.txt (overwritten each run).
REM Plain text (no ANSI colors) so it stays readable. Usage from the repo root:  .\test
setlocal
cd /d "%~dp0"

set NO_COLOR=1
set FORCE_COLOR=0

> test-results.txt echo ===== BACKEND (dotnet test + coverage) =====
dotnet test /p:CollectCoverage=true >> test-results.txt 2>&1

>> test-results.txt echo.
>> test-results.txt echo ===== FRONTEND (vitest run --coverage) =====
pushd TaskFlow.Web
call npm run coverage >> ..\test-results.txt 2>&1
popd

echo Done. Results + coverage written to test-results.txt
