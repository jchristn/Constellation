#!/usr/bin/env bash
# Build the controller image for linux/amd64 and linux/arm64/v8 with the default builder,
# push it to Docker Hub, then pull it into the local image store.
# Usage: ./build-docker.sh v1.0.0
set -u
cd "$(dirname "$0")"

if [ -z "${1:-}" ]; then
  echo "Provide a tag argument for the build."
  echo "Example: ./build-docker.sh v1.0.0"
  echo "Done"
  exit 1
fi

echo
echo "Building for linux/amd64 and linux/arm64/v8..."
echo "Step 1/2: single build, pushed to Docker Hub."
if ! docker buildx build -f ../Dockerfile --platform linux/amd64,linux/arm64/v8 --tag "jchristn77/constellation:$1" --tag jchristn77/constellation:latest --push ..; then
  echo "Done"
  exit 1
fi
echo "Step 2/2: pulling images into the local registry."
docker pull "jchristn77/constellation:$1"
docker pull jchristn77/constellation:latest
echo "Done"
