@ECHO OFF
IF "%1" == "" GOTO :Usage
ECHO.
ECHO ============================================================
ECHO Building ALL Constellation images with tag %1
ECHO ============================================================
CALL "%~dp0build-server.bat" %1
CALL "%~dp0build-dashboard.bat" %1
GOTO :Done

:Usage
ECHO Provide a tag argument for the build.
ECHO Example: build-all.bat v1.0.0

:Done
ECHO Done
@ECHO ON
