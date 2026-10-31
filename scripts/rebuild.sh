#!/usr/bin/env bash
set -e

# Run this script from any directory; it moves to the project root.
cd "$(dirname "$0")/.."

echo "Rebuilding TogetherCraft web container..."
sudo docker compose up -d --build --force-recreate togethercraft-web

echo
echo "Container status:"
sudo docker compose ps
