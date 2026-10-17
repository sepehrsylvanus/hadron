@echo off
title Hadron ERP - Local Runner
cd /d "%~dp0"

echo ============================================================
echo   Hadron ERP - Start all local services
echo ============================================================
echo.

REM ---------- 0) Prerequisites ----------
where docker >nul 2>&1
if errorlevel 1 (
  echo [ERROR] Docker CLI was not found in PATH.
  echo         Install Docker Desktop, then run this file again:
  echo         https://www.docker.com/products/docker-desktop/
  pause
  exit /b 1
)
where npm >nul 2>&1
if errorlevel 1 (
  echo [ERROR] Node.js / npm was not found in PATH.
  echo         Install Node.js, then run this file again:
  echo         https://nodejs.org/
  pause
  exit /b 1
)

REM --- Locate the .NET SDK even when it is not on PATH ---
where dotnet >nul 2>&1
if not errorlevel 1 goto dotnet_ready
for %%D in ("%ProgramFiles%\dotnet" "%ProgramFiles(x86)%\dotnet" "%USERPROFILE%\.dotnet" "%LOCALAPPDATA%\Microsoft\dotnet") do (
  if exist "%%~D\dotnet.exe" set "PATH=%%~D;%PATH%"
)
where dotnet >nul 2>&1
if errorlevel 1 (
  echo [ERROR] The .NET SDK was not found on this machine.
  echo         This project needs .NET SDK 10. Install it from:
  echo         https://dotnet.microsoft.com/download/dotnet/10.0
  echo         After installing, close this window and run run.bat again.
  pause
  exit /b 1
)
:dotnet_ready
for /f "delims=" %%V in ('dotnet --version') do echo [OK] Prerequisites found - .NET SDK %%V, Node/npm, Docker.

REM ---------- 1) Environment file (.env) ----------
if not exist ".env" (
  echo [ERROR] The .env file was not found. A copy was created from .env.example:
  copy /y ".env.example" ".env" >nul
  echo.
  echo   Replace POSTGRES_PASSWORD, MINIO_ACCESS_KEY, MINIO_SECRET_KEY
  echo   and IDENTITY_BOOTSTRAP_TOKEN in .env with local-only secrets,
  echo   then run this file again.
  echo.
  notepad ".env"
  pause
  exit /b 1
)

REM ----- Load .env into this terminal's environment -----
for /f "usebackq eol=# tokens=1,* delims==" %%A in (".env") do (
  if not "%%B"=="" set "%%A=%%B"
)
echo [OK] Environment variables loaded from .env.

REM ---------- 2) Docker Desktop ----------
docker info >nul 2>&1
if not errorlevel 1 goto docker_ready
echo [...] Docker Desktop is not running. Starting it...
start "" "C:\Program Files\Docker\Docker\Docker Desktop.exe"
set /a docker_tries=0
:wait_docker
timeout /t 5 /nobreak >nul
docker info >nul 2>&1
if not errorlevel 1 goto docker_ready
set /a docker_tries+=1
if %docker_tries% lss 24 goto wait_docker
echo [ERROR] Docker Desktop did not start in time. Start it manually, then run this file again.
pause
exit /b 1
:docker_ready
echo [OK] Docker is running.

REM ---------- 3) PostgreSQL and MinIO ----------
echo [...] Starting PostgreSQL and MinIO ...
docker compose up -d postgres minio
if errorlevel 1 (
  echo [ERROR] Failed to start the containers. See the output above.
  pause
  exit /b 1
)
set /a db_tries=0
:wait_db
docker compose exec -T postgres pg_isready -U %POSTGRES_USER% -d %POSTGRES_DB% >nul 2>&1
if not errorlevel 1 goto db_ready
set /a db_tries+=1
if %db_tries% geq 30 (
  echo [ERROR] PostgreSQL did not become ready. Check: docker compose ps
  pause
  exit /b 1
)
timeout /t 2 /nobreak >nul
goto wait_db
:db_ready
echo [OK] PostgreSQL and MinIO are up.

REM ---------- 4) Frontend dependencies (first run only) ----------
if not exist "src\frontend\node_modules" (
  echo [...] Installing frontend dependencies. This takes a while on the first run...
  pushd src\frontend
  call npm install
  popd
) else (
  echo [OK] Frontend dependencies are already installed.
)

REM ---------- 5) Build the API, then apply database migrations ----------
echo [...] Building the API. The first build can take a few minutes...
dotnet build src\backend\Hadron.Erp.Api --nologo -v minimal
if errorlevel 1 (
  echo.
  echo [ERROR] The API build failed. The compiler errors are printed above.
  echo         Common causes: NuGet restore needs internet access, or stale
  echo         build output - delete src\backend\Hadron.Erp.Api\bin and
  echo         src\backend\Hadron.Erp.Api\obj, then run this file again.
  pause
  exit /b 1
)
echo [OK] API build succeeded.
echo [...] Applying database migrations...
call dotnet tool restore
call dotnet tool run dotnet-ef database update --no-build --project src\backend\Hadron.Erp.Api --startup-project src\backend\Hadron.Erp.Api
if errorlevel 1 (
  echo [ERROR] Database migration failed. See the output above.
  pause
  exit /b 1
)
echo [OK] Database is up to date.

REM ---------- 6) API on port 5080 ----------
call :is_open 5080
if not errorlevel 1 (
  echo [OK] The API is already running on port 5080.
  goto api_done
)
echo [...] Starting the API in a separate window...
start "Hadron API (5080)" cmd /k dotnet run --project src\backend\Hadron.Erp.Api
call :wait_port 5080 120
if errorlevel 1 (
  echo [ERROR] The API did not start on port 5080. Check the "Hadron API" window.
  pause
  exit /b 1
)
echo [OK] API is ready: http://localhost:5080
:api_done

REM ---------- 7) Frontend on port 5173 ----------
call :is_open 5173
if not errorlevel 1 (
  echo [OK] The frontend is already running on port 5173.
  goto frontend_done
)
echo [...] Starting the frontend in a separate window...
start "Hadron Frontend (5173)" /D "src\frontend" cmd /k npm run dev
call :wait_port 5173 60
if errorlevel 1 (
  echo [ERROR] The frontend did not start on port 5173. Check the "Hadron Frontend" window.
  pause
  exit /b 1
)
echo [OK] Frontend is ready: http://127.0.0.1:5173
:frontend_done

REM ---------- 8) Open the browser ----------
start "" http://127.0.0.1:5173

echo.
echo ============================================================
echo   All services are up:
echo     Frontend : http://127.0.0.1:5173
echo     API      : http://localhost:5080
echo     MinIO    : http://127.0.0.1:9001
echo     Postgres : 127.0.0.1:5432
echo.
echo   To stop: close the "Hadron API" and "Hadron Frontend" windows.
echo   To also stop Docker services: docker compose down
echo ============================================================
echo.
pause
exit /b 0

REM ================= Helpers =================
:is_open
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $c = New-Object Net.Sockets.TcpClient; $c.Connect('127.0.0.1', %~1); $c.Close(); exit 0 } catch { exit 1 }" >nul 2>&1
exit /b %errorlevel%

:wait_port
set /a _tries=0
:wait_port_loop
call :is_open %~1
if not errorlevel 1 exit /b 0
set /a _tries+=1
if %_tries% geq %~2 exit /b 1
timeout /t 2 /nobreak >nul
goto wait_port_loop
