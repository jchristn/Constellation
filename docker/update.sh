#!/usr/bin/env bash
# Pull the latest published images and recreate the stack. Non-destructive: named volumes are preserved.
cd "$(dirname "$0")"
docker compose -f compose.yaml pull
docker compose -f compose.yaml down
docker compose -f compose.yaml up -d
docker ps -a
