@echo off
REM ============================================================
REM  Hadron ERP - one-click local launcher (double-click to run)
REM  Steps: Docker Desktop -> postgres+minio -> API -> frontend
REM  Place this file next to docker-compose.yml (project root).
REM ============================================================
setlocal EnableDelayedExpansion
cd /d "%~dp0"
title Hadron ERP - Launcher

REM ---------- 0) Locate .NET SDK (works even when not on PATH) ----------
set "DOTNET_EXE="
for %%D in ("%USERPROFILE%\.dotnet" "%ProgramFiles%\dotnet" "%ProgramFiles(x86)%\dotnet" "%LOCALAPPDATA%\Microsoft\dotnet") do (
  if exist "%%~D\dotnet.exe" set "DOTNET_EXE=%%~D\dotnet.exe"
)
if not defined DOTNET_EXE (
  where dotnet >nul 2>nul
  if errorlevel 1 (
    echo [ERROR] .NET SDK 10 not found. Install it from:
    echo         https://dotnet.microsoft.com/download/dotnet/10.0
    echo         Then close this window and run this file again.
    pause
    exit /b 1
  )
  set "DOTNET_EXE=dotnet"
)
for /f "delims=" %%V in ('"%DOTNET_EXE%" --version 2^>nul') do set "DOTNET_VER=%%V"
echo [OK] .NET SDK %DOTNET_VER% at %DOTNET_EXE%

REM Prefer WinGet Node 26 (project requires node ^>=26 ^<27), fallback to Program Files nodejs v26.
set "NODE_DIR="
for /f "delims=" %%N in ('dir /b /ad "%LOCALAPPDATA%\Microsoft\WinGet\Packages\OpenJS.NodeJS_*" 2^>nul') do (
  for /f "delims=" %%V in ('dir /b /ad "%LOCALAPPDATA%\Microsoft\WinGet\Packages\%%N\node-v26*" 2^>nul') do (
    if exist "%LOCALAPPDATA%\Microsoft\WinGet\Packages\%%N\%%V\node.exe" set "NODE_DIR=%LOCALAPPDATA%\Microsoft\WinGet\Packages\%%N\%%V"
  )
)
if not defined NODE_DIR (
  if exist "%ProgramFiles%\nodejs\node.exe" (
    "%ProgramFiles%\nodejs\node.exe" --version 2>nul | findstr /b "v26" >nul
    if not errorlevel 1 set "NODE_DIR=%ProgramFiles%\nodejs"
  )
)
if not defined NODE_DIR (
  echo [ERROR] Node.js 26 not found. Install Node 26.x, then run again:
  echo         https://nodejs.org/
  pause
  exit /b 1
)
set "PATH=%NODE_DIR%;%PATH%"
echo [OK] Node at %NODE_DIR%

if not exist ".env" (
  echo [ERROR] .env file not found. Create it first:
  echo         copy .env.example .env
  echo         Then fill in POSTGRES_PASSWORD, MINIO keys and IDENTITY_BOOTSTRAP_TOKEN.
  pause
  exit /b 1
)
if not exist "docker-compose.yml" (
  echo [ERROR] docker-compose.yml not found. Put this .bat in the project root.
  pause
  exit /b 1
)

echo Loading .env into environment for the API process...
for /f "usebackq eol=# tokens=1* delims==" %%A in (".env") do (
  if not "%%A"=="" if not "%%B"=="" set "%%A=%%B"
)
if "%POSTGRES_DB%"=="" (
  echo [ERROR] .env was not loaded correctly ^(POSTGRES_DB empty^).
  pause
  exit /b 1
)
set "ASPNETCORE_ENVIRONMENT=Development"
set "DOTNET_ENVIRONMENT=Development"
set "ASPNETCORE_URLS=http://127.0.0.1:5080"
echo [OK] .env loaded.

echo === [1/5] Checking Docker Desktop ===
docker info >nul 2>nul
if not errorlevel 1 goto dockeready
echo Docker daemon is down - launching Docker Desktop...
start "" "C:\Program Files\Docker\Docker\Docker Desktop.exe"
set /a _wait=0
:waitdocker
timeout /t 10 /nobreak >nul
docker info >nul 2>nul
if not errorlevel 1 goto dockeready
set /a _wait+=1
if !_wait! GEQ 18 (
  echo [ERROR] Docker Desktop did not start in 3 minutes.
  echo         Start it manually, then run this file again.
  pause
  exit /b 1
)
echo Still waiting for Docker... ^(!_wait!x10s^)
goto waitdocker
:dockeready
echo [OK] Docker is ready.

echo === [2/5] Starting postgres + minio ===
docker compose up -d postgres minio
if errorlevel 1 (
  echo [ERROR] docker compose failed.
  pause
  exit /b 1
)

echo === [3/5] Waiting for postgres + minio health ===
set /a _wait=0
:waithealth
set "PG=?"
set "MIO=?"
for /f %%S in ('docker inspect --format "{{.State.Health.Status}}" hadron-erp-postgres-1 2^>nul') do set "PG=%%S"
for /f %%S in ('docker inspect --format "{{.State.Health.Status}}" hadron-erp-minio-1 2^>nul') do set "MIO=%%S"
if "%PG%"=="healthy" if "%MIO%"=="healthy" goto healthy
set /a _wait+=1
if !_wait! GEQ 18 (
  echo [ERROR] Containers not healthy after 3 minutes. PG=%PG% MINIO=%MIO%
  echo         Check: docker compose ps   +   docker compose logs postgres minio
  pause
  exit /b 1
)
echo Waiting... postgres=%PG% minio=%MIO% ^(!_wait!x10s^)
timeout /t 10 /nobreak >nul
goto waithealth
:healthy
echo [OK] postgres + minio are healthy.

REM ---------- 3b) Apply EF migrations (skipped when DB is already current) ----------
echo --- Applying database migrations ^(skipped if up to date^) ---
call "%DOTNET_EXE%" tool restore
if errorlevel 1 (
  echo [WARN] dotnet tool restore failed - migrations will be skipped.
  echo         The API may fail if the database schema is outdated.
  goto migrdone
)
call "%DOTNET_EXE%" tool run dotnet-ef database update --no-build --project src\backend\Hadron.Erp.Api --startup-project src\backend\Hadron.Erp.Api
if errorlevel 1 (
  echo [WARN] Migration step failed. If the schema changed, the API may fail.
  echo         Check the output above; a common fix is deleting
  echo         src\backend\Hadron.Erp.Api\bin and obj, then running again.
  pause
) else (
  echo [OK] Database is up to date.
)
:migrdone

echo === [4/5] Starting API on http://127.0.0.1:5080 ===
powershell -NoProfile -Command "$c=[Net.Sockets.TcpClient]::new(); try { if ($c.ConnectAsync('127.0.0.1',5080).Wait(800) -and $c.Connected) { exit 0 } else { exit 1 } } catch { exit 1 } finally { $c.Dispose() }" >nul 2>nul
if not errorlevel 1 goto apiskip
start "Hadron ERP - API" cmd /k ""%DOTNET_EXE%" run --project src\backend\Hadron.Erp.Api --no-launch-profile"
echo Waiting for API health...
set /a _wait=0
:waitapi
timeout /t 5 /nobreak >nul
curl -s -m 5 http://127.0.0.1:5080/health/live >nul 2>nul
if not errorlevel 1 goto apiready
set /a _wait+=1
if !_wait! GEQ 36 (
  echo [ERROR] API did not become healthy in 3 minutes. Check the "Hadron ERP - API" window.
  echo         First build can take a few minutes - wait, then run this file again.
  pause
  exit /b 1
)
echo Still waiting for API... ^(!_wait!x5s^) - first build can take a few minutes.
goto waitapi
:apiskip
echo [OK] API port 5080 already in use - assuming API is running, skipping.
:apiready
echo [OK] API is up.

echo === [5/5] Starting frontend ===
if not exist "src\frontend\node_modules" (
  echo Frontend dependencies missing - installing ^(first run only, takes a while^)...
  pushd src\frontend
  call npm install
  popd
  if errorlevel 1 (
    echo [ERROR] npm install failed. Check the output above.
    pause
    exit /b 1
  )
)
powershell -NoProfile -Command "$c=[Net.Sockets.TcpClient]::new(); try { if ($c.ConnectAsync('127.0.0.1',5173).Wait(800) -and $c.Connected) { exit 0 } else { exit 1 } } catch { exit 1 } finally { $c.Dispose() }" >nul 2>nul
if not errorlevel 1 goto viteskip
start "Hadron ERP - Frontend" /d "%~dp0src\frontend" cmd /k npm run dev
echo Waiting for frontend...
set /a _wait=0
:waitvite
timeout /t 3 /nobreak >nul
curl -s -m 5 -o nul -w "%%{http_code}" http://127.0.0.1:5173/ 2>nul | findstr "200" >nul
if not errorlevel 1 goto viteready
set /a _wait+=1
if !_wait! GEQ 40 goto vitewarn
goto waitvite
:viteskip
echo [OK] Frontend port 5173 already in use - assuming frontend is running, skipping.
goto viteready
:vitewarn
echo [WARN] Frontend did not answer in 2 minutes. Check the "Hadron ERP - Frontend" window.
goto opendone
:viteready
echo [OK] Frontend is up.
:opendone

echo.
echo ============================================================
echo  Hadron ERP is running:
echo    Frontend : http://127.0.0.1:5173
echo    API      : http://127.0.0.1:5080  ^(ready: /health/ready^)
echo    MinIO    : http://127.0.0.1:9001
echo  Two extra windows ^(API + Frontend^) were opened - keep them open.
echo  Close them to stop the servers. DB data stays in docker volumes.
echo ============================================================
start "" http://127.0.0.1:5173/
pause
