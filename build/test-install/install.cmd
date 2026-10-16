@echo off
rem Installs the Elynt Optimizer background service from this test build.
rem Temporary until the real installer exists. Right-click and choose "Run as administrator".

net session >nul 2>&1
if errorlevel 1 (
    echo Please right-click install.cmd and choose "Run as administrator".
    pause
    exit /b 1
)

set "TARGET=%ProgramFiles%\ElyntOptimizer\service"

sc.exe query ElyntOptimizer >nul 2>&1
if not errorlevel 1 (
    echo Removing the previous test install first...
    sc.exe stop ElyntOptimizer >nul 2>&1
    timeout /t 3 /nobreak >nul
    sc.exe delete ElyntOptimizer >nul
    timeout /t 2 /nobreak >nul
)

rem The service runs as SYSTEM, so it runs from Program Files where normal users can't change it.
robocopy "%~dp0service" "%TARGET%" /MIR /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 (
    echo Copying the service failed.
    pause
    exit /b 1
)

sc.exe create ElyntOptimizer binPath= "\"%TARGET%\Elynt.Service.exe\"" start= auto DisplayName= "Elynt Optimizer" >nul
sc.exe description ElyntOptimizer "Applies and undoes the changes you choose in Elynt's Windows Optimizer." >nul
sc.exe start ElyntOptimizer >nul
if errorlevel 1 (
    echo The service was installed but did not start. Check %ProgramData%\ElyntOptimizer\logs.
    pause
    exit /b 1
)

echo.
echo Installed and running. Now open app\ElyntOptimizer.exe normally (not as administrator).
pause
