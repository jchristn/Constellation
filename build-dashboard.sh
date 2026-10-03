#!/usr/bin/env bash
# Build the dashboard image for linux/amd64 and linux/arm64/v8 on the cloud builder,
# push it to Docker Hub, then pull it into the local image store.
# Usage: ./build-dashboard.sh v1.0.0
set -u
cd "$(dirname "$0")"

if [ -z "${1:-}" ]; then
  echo "Provide a tag argument for the build."
  echo "Example: ./build-dashboard.sh v1.0.0"
  echo "Done"
  exit 1
fi

echo
echo "Building dashboard for linux/amd64 and linux/arm64/v8..."
echo "Step 1/2: single cloud build, pushed to Docker Hub."
if ! docker buildx build --builder cloud-jchristn77-jchristn77 -f dashboard/Dockerfile --platform linux/amd64,linux/arm64/v8 --tag "jchristn77/constellation-dashboard:$1" --tag jchristn77/constellation-dashboard:latest --push dashboard/; then
  echo "Done"
  exit 1
fi
echo "Step 2/2: pulling images into the local registry (from Docker Hub, not the cloud builder)."
docker pull "jchristn77/constellation-dashboard:$1"
docker pull jchristn77/constellation-dashboard:latest
echo "Done"
