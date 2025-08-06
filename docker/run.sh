if [ -z "${IMG_TAG}" ]; then
  IMG_TAG='v1.0.0'
fi

echo Using image tag $IMG_TAG

if [ ! -f "constellation.json" ]
then
  echo Configuration file constellation.json not found.
  exit
fi

# Items that require persistence
#   constellation.json
#   logs/

# Argument order matters!

docker run \
  -p 8000:8000 \
  -t \
  -i \
  -e "TERM=xterm-256color" \
  -v ./constellation.json:/app/constellation.json \
  -v ./logs/:/app/logs/ \
  jchristn/constellation:$IMG_TAG

