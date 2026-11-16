@echo off
rem Adds the OneClick self-signed certificate to the current user's trusted stores.
rem After this SmartScreen stops complaining about "unknown publisher" on this PC.
setlocal
cd /d "%~dp0"
if not exist "OneClick-cert.cer" (
    echo OneClick-cert.cer not found next to this script.
    pause
    exit /b 1
)
echo Adding certificate to Trusted Root...
certutil -addstore -user Root "OneClick-cert.cer"
echo Adding certificate to Trusted Publishers...
certutil -addstore -user TrustedPublisher "OneClick-cert.cer"
echo.
echo Done. You can delete this script and the .cer file.
pause
