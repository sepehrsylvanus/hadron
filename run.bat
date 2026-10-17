@echo off
chcp 65001 >nul
title Hadron ERP - Local Runner
cd /d "%~dp0"

echo ============================================================
echo   Hadron ERP - اجرای لوکال همه سرویس‌ها
echo ============================================================
echo.

REM ---------- 1) فایل محیطی .env ----------
if not exist ".env" (
  echo [خطا] فایل .env پیدا نشد. از روی نمونه ساخته شد:
  copy /y ".env.example" ".env" >nul
  echo.
  echo   مقادیر POSTGRES_PASSWORD و MINIO_ACCESS_KEY و MINIO_SECRET_KEY
  echo   و IDENTITY_BOOTSTRAP_TOKEN را در فایل .env عوض کنید،
  echo   بعد دوباره run.bat را اجرا کنید.
  echo.
  notepad ".env"
  pause
  exit /b 1
)

REM ----- خواندن .env و ست‌کردن متغیرهای محیطی همین ترمینال -----
for /f "usebackq eol=# tokens=1,* delims==" %%A in (".env") do (
  if not "%%B"=="" set "%%A=%%B"
)
echo [OK] متغیرهای .env خوانده شد.

REM ---------- 2) Docker Desktop ----------
docker info >nul 2>&1
if not errorlevel 1 goto docker_ready
echo [...] Docker Desktop در حال اجرا نیست؛ در حال باز کردنش...
start "" "C:\Program Files\Docker\Docker\Docker Desktop.exe"
set /a docker_tries=0
:wait_docker
timeout /t 5 /nobreak >nul
docker info >nul 2>&1
if not errorlevel 1 goto docker_ready
set /a docker_tries+=1
if %docker_tries% lss 24 goto wait_docker
echo [خطا] Docker Desktop بالا نیامد. دستی اجراش کنید و دوباره run.bat را بزنید.
pause
exit /b 1
:docker_ready
echo [OK] Docker آماده است.

REM ---------- 3) PostgreSQL و MinIO ----------
echo [...] در حال اجرای PostgreSQL و MinIO ...
docker compose up -d postgres minio
if errorlevel 1 (
  echo [خطا] اجرای کانتینرها ناموفق بود.
  pause
  exit /b 1
)
set /a db_tries=0
:wait_db
docker compose exec -T postgres pg_isready -U %POSTGRES_USER% -d %POSTGRES_DB% >nul 2>&1
if not errorlevel 1 goto db_ready
set /a db_tries+=1
if %db_tries% geq 30 (
  echo [خطا] PostgreSQL آماده نشد. وضعیت: docker compose ps
  pause
  exit /b 1
)
timeout /t 2 /nobreak >nul
goto wait_db
:db_ready
echo [OK] PostgreSQL و MinIO بالا هستند.

REM ---------- 4) وابستگی‌های فرانت‌اند (فقط بار اول) ----------
if not exist "src\frontend\node_modules" (
  echo [...] در حال نصب وابستگی‌های فرانت‌اند، فقط بار اول طول می‌کشد...
  pushd src\frontend
  call npm install
  popd
) else (
  echo [OK] وابستگی‌های فرانت‌اند نصب هستند.
)

REM ---------- 5) Migration دیتابیس ----------
echo [...] در حال اعمال Migration های دیتابیس...
call dotnet tool restore
call dotnet tool run dotnet-ef database update --project src\backend\Hadron.Erp.Api --startup-project src\backend\Hadron.Erp.Api
if errorlevel 1 (
  echo [خطا] Migration ناموفق بود. خروجی بالا را بررسی کنید.
  pause
  exit /b 1
)
echo [OK] دیتابیس به‌روز است.

REM ---------- 6) API روی پورت 5080 ----------
call :is_open 5080
if not errorlevel 1 (
  echo [OK] API از قبل روی پورت 5080 در حال اجراست.
  goto api_done
)
echo [...] در حال اجرای API در یک پنجره جدا...
start "Hadron API (5080)" cmd /k dotnet run --project src\backend\Hadron.Erp.Api
call :wait_port 5080 120
if errorlevel 1 (
  echo [خطا] API روی پورت 5080 بالا نیامد. پنجره Hadron API را بررسی کنید.
  pause
  exit /b 1
)
echo [OK] API آماده است: http://localhost:5080
:api_done

REM ---------- 7) فرانت‌اند روی پورت 5173 ----------
call :is_open 5173
if not errorlevel 1 (
  echo [OK] فرانت‌اند از قبل روی پورت 5173 در حال اجراست.
  goto frontend_done
)
echo [...] در حال اجرای فرانت‌اند در یک پنجره جدا...
start "Hadron Frontend (5173)" /D "src\frontend" cmd /k npm run dev
call :wait_port 5173 60
if errorlevel 1 (
  echo [خطا] فرانت‌اند روی پورت 5173 بالا نیامد. پنجره Hadron Frontend را بررسی کنید.
  pause
  exit /b 1
)
echo [OK] فرانت‌اند آماده است: http://127.0.0.1:5173
:frontend_done

REM ---------- 8) باز کردن مرورگر ----------
start "" http://127.0.0.1:5173

echo.
echo ============================================================
echo   همه سرویس‌ها بالا هستند:
echo     Frontend : http://127.0.0.1:5173
echo     API      : http://localhost:5080
echo     MinIO    : http://127.0.0.1:9001
echo     Postgres : 127.0.0.1:5432
echo.
echo   برای توقف: پنجره‌های Hadron API و Hadron Frontend را ببندید.
echo   برای توقف کامل Docker هم: docker compose down
echo ============================================================
echo.
pause
exit /b 0

REM ================= توابع کمکی =================
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
