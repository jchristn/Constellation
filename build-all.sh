#!/usr/bin/env bash
# Build and push ALL Constellation images (controller and dashboard) with the given tag.
# Usage: ./build-all.sh v1.0.0
set -u
cd "$(dirname "$0")"

if [ -z "${1:-}" ]; then
  echo "Provide a tag argument for the build."
  echo "Example: ./build-all.sh v1.0.0"
  echo "Done"
  exit 1
fi

echo
echo "============================================================"
echo "Building ALL Constellation images with tag $1"
echo "============================================================"
status=0
./build-server.sh "$1" || status=1
./build-dashboard.sh "$1" || status=1
echo "Done"
exit $status
