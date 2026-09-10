@ECHO OFF
IF "%1" == "" GOTO :Usage
ECHO.
ECHO Building server for linux/amd64 and linux/arm64/v8...
ECHO Step 1/2: single cloud build, pushed to Docker Hub.
docker buildx build --builder cloud-jchristn77-jchristn77 -f Dockerfile --platform linux/amd64,linux/arm64/v8 --tag jchristn77/constellation:%1 --tag jchristn77/constellation:latest --push .
IF ERRORLEVEL 1 GOTO :Done
ECHO Step 2/2: pulling images into the local registry (from Docker Hub, not the cloud builder).
docker pull jchristn77/constellation:%1
docker pull jchristn77/constellation:latest

GOTO :Done

:Usage
ECHO Provide a tag argument for the build.
ECHO Example: build-server.bat v1.0.0

:Done
ECHO Done
@ECHO ON
