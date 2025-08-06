@echo off

IF "%1" == "" GOTO :Usage

if not exist constellation.json (
  echo Configuration file constellation.json not found.
  exit /b 1
)

REM Items that require persistence
REM   constellation.json
REM   logs/

REM Argument order matters!

docker run ^
  -p 8000:8000 ^
  -t ^
  -i ^
  -e "TERM=xterm-256color" ^
  -v .\constellation.json:/app/constellation.json ^
  -v .\logs\:/app/logs/ ^
  jchristn/constellation:%1

GOTO :Done

:Usage
ECHO Provide one argument indicating the tag. 
ECHO Example: dockerrun.bat v1.0.0
:Done
@echo on
