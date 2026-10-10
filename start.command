#!/bin/bash
cd "$(dirname "$0")"
if ! docker info >/dev/null 2>&1; then
  echo "Starting Docker Desktop, please wait..."
  open -a Docker
  until docker info >/dev/null 2>&1; do sleep 2; done
fi
stop_app() { echo; echo "Stopping WebhookService..."; docker compose stop >/dev/null 2>&1; echo "Stopped."; exit 0; }
trap stop_app INT
echo "Starting WebhookService... (the first run can take a few minutes)"
docker compose up -d >/dev/null 2>&1 || docker compose up -d
for i in $(seq 1 90); do
  curl -s -o /dev/null http://localhost:5080 && break
  sleep 1
done
open http://localhost:5080/scalar
echo
echo "WebhookService is running at http://localhost:5080"
echo "Press Enter or Ctrl + C in this window to stop it."
read
stop_app
