@ECHO OFF
REM Pull the latest published images and recreate the stack. Non-destructive: named volumes are preserved.
CD /D "%~dp0"
docker compose -f compose.yaml pull
docker compose -f compose.yaml down
docker compose -f compose.yaml up -d
docker ps -a
@ECHO ON
