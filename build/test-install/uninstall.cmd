@echo off
rem Removes the test build's background service. Right-click and choose "Run as administrator".
rem Turn your tweaks off in the app first if you want your original settings back.

net session >nul 2>&1
if errorlevel 1 (
    echo Please right-click uninstall.cmd and choose "Run as administrator".
    pause
    exit /b 1
)

sc.exe stop ElyntOptimizer >nul 2>&1
timeout /t 3 /nobreak >nul
sc.exe delete ElyntOptimizer >nul 2>&1
rmdir /s /q "%ProgramFiles%\ElyntOptimizer" 2>nul

echo.
echo Removed. The undo journal and logs stay in %ProgramData%\ElyntOptimizer.
pause
