@ECHO OFF
IF "%1" == "" GOTO :Usage
ECHO.
ECHO Building for linux/amd64 and linux/arm64/v8...
docker buildx build --builder cloud-jchristn77-jchristn77 -f dashboard/Dockerfile --platform linux/amd64,linux/arm64/v8 --tag jchristn77/constellation-dashboard:%1 --tag jchristn77/constellation-dashboard:latest --push dashboard/

GOTO :Done

:Usage
ECHO Provide a tag argument for the build.
ECHO Example: build-dashboard.bat v1.0.0

:Done
ECHO Done
@ECHO ON
