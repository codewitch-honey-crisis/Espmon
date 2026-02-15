@echo off
if not defined IDF_PATH (
    echo ERROR: IDF_PATH environment variable not set!
    echo Please install ESP-IDF and run the installer's environment setup.
    echo Or run this from an ESP-IDF terminal.
    exit /b 1
)

echo Activating ESP-IDF environment...
call "%IDF_PATH%\export.bat"

echo Building all board configurations...
cmake -P build_all.cmake

if %errorlevel% neq 0 (
    echo Build failed!
    exit /b %errorlevel%
)

echo.
echo Build complete! Binaries are in firmware/