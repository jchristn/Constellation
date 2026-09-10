@ECHO OFF
IF "%1" == "" GOTO :Usage
ECHO.
ECHO Building for linux/amd64 and linux/arm64/v8...
ECHO Step 1/2: single build, pushed to Docker Hub.
docker buildx build -f ../Dockerfile --platform linux/amd64,linux/arm64/v8 --tag jchristn77/constellation:%1 --tag jchristn77/constellation:latest --push ..
IF ERRORLEVEL 1 GOTO :Done
ECHO Step 2/2: pulling images into the local registry.
docker pull jchristn77/constellation:%1
docker pull jchristn77/constellation:latest

GOTO :Done

:Usage
ECHO Provide a tag argument for the build.
ECHO Example: dockerbuild.bat v1.0.0

:Done
ECHO Done
@ECHO ON
