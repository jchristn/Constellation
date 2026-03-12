@ECHO OFF
ECHO.
ECHO Building Constellation Dashboard...
ECHO.

IF NOT EXIST dashboard (
    ECHO ERROR: dashboard directory not found.
    GOTO :Done
)

cd dashboard

IF NOT EXIST node_modules (
    ECHO Installing dependencies...
    call npm install
    IF ERRORLEVEL 1 (
        ECHO ERROR: npm install failed.
        cd ..
        GOTO :Done
    )
)

ECHO Building production bundle...
call npm run build
IF ERRORLEVEL 1 (
    ECHO ERROR: Build failed.
    cd ..
    GOTO :Done
)

cd ..

ECHO.
ECHO Build complete. Output is in dashboard\dist\
ECHO.
ECHO To run in development mode:
ECHO   cd dashboard
ECHO   npm run dev
ECHO.

:Done
ECHO Done
@ECHO ON
