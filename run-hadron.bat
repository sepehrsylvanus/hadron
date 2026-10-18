@echo off
REM Compatibility entry point. Existing ERP now lives in main-erp.
call "%~dp0main-erp\run-hadron.bat"
exit /b %errorlevel%
