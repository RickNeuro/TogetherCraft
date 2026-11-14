#!/usr/bin/env bash
set -Eeuo pipefail

ROOT_DIR="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
OUTPUT_DIR="$ROOT_DIR/wwwroot/img/game-assets"
mkdir -p "$OUTPUT_DIR"

download_png() {
    local url="$1"
    local destination="$2"
    local temporary="${destination}.tmp"

    echo "Downloading $(basename "$destination")..."
    rm -f "$temporary"
    curl --fail --location --silent --show-error \
        --retry 3 --retry-delay 2 --connect-timeout 15 --max-time 120 \
        --user-agent "TogetherCraft game asset downloader" \
        --output "$temporary" "$url"

    local signature
    signature="$(head -c 8 "$temporary" | od -An -tx1 | tr -d ' \n')"
    if [[ "$signature" != "89504e470d0a1a0a" ]]; then
        rm -f "$temporary"
        echo "Downloaded file is not a PNG: $url" >&2
        return 1
    fi

    if [[ ! -s "$temporary" ]]; then
        rm -f "$temporary"
        echo "Downloaded file is empty: $url" >&2
        return 1
    fi

    mv "$temporary" "$destination"
    echo "Saved $destination"
}

download_png \
    "https://blockrender.dev/render/block/mossy_cobblestone.png?size=512" \
    "$OUTPUT_DIR/mossy-cobblestone.png"

download_png \
    "https://raw.githubusercontent.com/Creators-of-Create/Create/4148fd8d5b05c85d88947edc00fb495a5074f57d/src/main/resources/assets/create/textures/block/large_cogwheel.png" \
    "$OUTPUT_DIR/create-large-cogwheel.png"

download_png \
    "https://wiki.factorio.com/images/Factorio-logo.png" \
    "$OUTPUT_DIR/factorio-logo.png"

echo "All original game artwork is ready."
