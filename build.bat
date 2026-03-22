@echo off
setlocal enabledelayedexpansion
echo ============================================
echo Belmont Labs - SyncDAT Build Script
echo ============================================
echo.

REM Check if .NET SDK is installed
where dotnet >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: .NET SDK not found!
    echo Please install .NET SDK 8.0 or later from: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

REM Display .NET version
echo Checking .NET SDK version...
dotnet --version
echo.

REM Clean old build artifacts first
echo Cleaning old build artifacts...
if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"
echo Clean complete.
echo.

REM Check for icon.ico
if exist "icon.ico" (
    echo [OK] Found icon.ico - will be included in build
) else (
    echo [WARNING] icon.ico not found - application will use default Windows icon
    echo Place icon.ico in the project directory to use a custom icon
)
echo.

REM Menu for build options
echo Build Options:
echo.
echo 1. Framework-dependent executable (SMALLEST - ~200 KB, requires .NET 8 installed)
echo    - User must have .NET 8 Runtime installed
echo    - Smallest file size
echo    - Best for: Distribution to users who already have .NET
echo.
echo 2. Self-contained single file (RECOMMENDED - ~65-70 MB)
echo    - Everything included in one EXE
echo    - No .NET installation required
echo    - Best for: Most users, maximum compatibility
echo.
echo 3. Self-contained with ReadyToRun (FASTEST STARTUP - ~85-95 MB)
echo    - Pre-compiled for faster startup
echo    - Larger file size
echo    - Best for: When startup speed is critical
echo.
set /p choice="Select build option (1-3): "

if "%choice%"=="1" goto framework_dependent
if "%choice%"=="2" goto self_contained
if "%choice%"=="3" goto self_contained_r2r
echo Invalid choice!
pause
exit /b 1

:framework_dependent
echo.
echo Building framework-dependent deployment...
echo (Requires .NET 8 Runtime to be installed on target machine)
echo.

REM For framework-dependent, we DON'T specify runtime identifier
REM This creates a portable executable that works across platforms
dotnet publish -c Release ^
    --no-self-contained ^
    -o ./bin/Release/Publish

goto build_complete

:self_contained
echo.
echo Building self-contained single-file deployment...
echo (Includes .NET runtime, no installation required)
echo.

dotnet publish -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o ./bin/Release/Publish

goto build_complete

:self_contained_r2r
echo.
echo Building self-contained deployment with ReadyToRun...
echo (Fastest startup, larger file size)
echo.

dotnet publish -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:PublishReadyToRun=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o ./bin/Release/Publish

goto build_complete

:build_complete
echo.
if %errorlevel% neq 0 (
    echo ============================================
    echo BUILD FAILED!
    echo ============================================
    pause
    exit /b 1
)

echo ============================================
echo BUILD SUCCESSFUL!
echo ============================================
echo.
echo Output location: .\bin\Release\Publish
echo Main executable: SyncDAT.exe
echo.

REM Display file size
if exist ".\bin\Release\Publish\SyncDAT.exe" (
    for %%A in (".\bin\Release\Publish\SyncDAT.exe") do (
        set size=%%~zA
        set /a sizeMB=!size!/1048576
        set /a sizeKB=!size!/1024
        
        if !sizeMB! GTR 0 (
            echo File size: !sizeMB! MB ^(!sizeKB! KB^)
        ) else (
            echo File size: !sizeKB! KB
        )
    )
) else (
    echo WARNING: SyncDAT.exe not found in output directory
)

echo.

REM Copy icon.ico to output if it exists
if exist "icon.ico" (
    echo Copying icon.ico to output directory...
    copy /Y "icon.ico" ".\bin\Release\Publish\icon.ico" >nul 2>&1
    if %errorlevel% equ 0 (
        echo [OK] Icon file copied successfully.
        echo.
        echo IMPORTANT: When distributing the application, make sure to include:
        echo   - SyncDAT.exe
        echo   - icon.ico (in the same directory as the .exe)
    ) else (
        echo [ERROR] Failed to copy icon.ico
    )
) else (
    echo.
    echo [WARNING] icon.ico not found in project directory!
    echo The application will use the default system icon.
    echo To use a custom icon:
    echo   1. Place icon.ico in the project directory
    echo   2. Rebuild the application
    echo   3. Distribute both SyncDAT.exe and icon.ico together
)

echo.
echo Build Summary:
echo --------------
if "%choice%"=="1" (
    echo Build Type: Framework-dependent
    echo Deployment: Requires .NET 8 Runtime on target machine
    echo File Size: ~200 KB
) else if "%choice%"=="2" (
    echo Build Type: Self-contained single file
    echo Deployment: No runtime installation required
    echo File Size: ~65-70 MB
    echo Recommended: YES - best balance of size and compatibility
) else if "%choice%"=="3" (
    echo Build Type: Self-contained with ReadyToRun
    echo Deployment: No runtime installation required
    echo File Size: ~85-95 MB
    echo Benefit: Faster startup time
)

echo.
echo NOTE: Windows Forms applications cannot use assembly trimming,
echo so the ~65-70 MB size for self-contained builds is expected.
echo This is normal for WinForms applications.
echo.

pause