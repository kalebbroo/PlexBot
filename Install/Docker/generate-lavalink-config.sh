#!/bin/sh
# generate-lavalink-config.sh
# Assembles lavalink.application.yml from a base template + extension plugin fragments.
#
# Each extension that needs a Lavalink plugin ships a lavalink.plugin.yml with:
#   plugin:        -> appended to lavalink.plugins[] array
#   pluginConfig:  -> deep-merged under top-level plugins: key
#   sources:       -> merged into lavalink.server.sources
#
# Usage: generate-lavalink-config.sh <extensions_dir> <output_dir> [base_config]

set -e

EXTENSIONS_DIR="${1:-.}"
OUTPUT_DIR="${2:-.}"
BASE_CONFIG="${3:-$OUTPUT_DIR/lavalink.base.yml}"
OUTPUT_FILE="$OUTPUT_DIR/lavalink.application.yml"

# Verify base config exists
if [ ! -f "$BASE_CONFIG" ]; then
    echo "[lavalink-config] ERROR: Base config not found at $BASE_CONFIG"
    exit 1
fi

# Verify yq is available
if ! command -v yq >/dev/null 2>&1; then
    echo "[lavalink-config] ERROR: yq is required but not found"
    exit 1
fi

# Start from a clean copy of the base template
cp "$BASE_CONFIG" "$OUTPUT_FILE"
echo "[lavalink-config] Starting from base template: $BASE_CONFIG"

FRAGMENT_COUNT=0

# Scan each extension directory for lavalink.plugin.yml fragments
for ext_dir in "$EXTENSIONS_DIR"/*/; do
    # Skip if not a directory
    [ -d "$ext_dir" ] || continue

    ext_name="$(basename "$ext_dir")"

    # Skip disabled extensions
    case "$ext_name" in *.disabled) continue ;; esac

    fragment="$ext_dir/lavalink.plugin.yml"
    [ -f "$fragment" ] || continue

    echo "[lavalink-config] Merging fragment from: $ext_name"

    # 1. Append plugin dependency to lavalink.plugins[] array (if present)
    has_plugin=$(yq '.plugin // ""' "$fragment")
    if [ -n "$has_plugin" ] && [ "$has_plugin" != "null" ] && [ "$has_plugin" != "" ]; then
        yq -i ".lavalink.plugins += [load(\"$fragment\").plugin]" "$OUTPUT_FILE"
    fi

    # 2. Deep-merge plugin configuration under top-level 'plugins' (if present)
    has_config=$(yq '.pluginConfig // ""' "$fragment")
    if [ -n "$has_config" ] && [ "$has_config" != "null" ] && [ "$has_config" != "" ]; then
        yq -i ".plugins *= load(\"$fragment\").pluginConfig" "$OUTPUT_FILE"
    fi

    # 3. Merge source overrides into lavalink.server.sources (if present)
    has_sources=$(yq '.sources // ""' "$fragment")
    if [ -n "$has_sources" ] && [ "$has_sources" != "null" ] && [ "$has_sources" != "" ]; then
        yq -i ".lavalink.server.sources *= load(\"$fragment\").sources" "$OUTPUT_FILE"
    fi

    FRAGMENT_COUNT=$((FRAGMENT_COUNT + 1))
done

# Deduplicate plugin dependencies (same plugin used by multiple extensions)
if [ "$FRAGMENT_COUNT" -gt 0 ]; then
    yq -i '.lavalink.plugins |= unique_by(.dependency)' "$OUTPUT_FILE"
    echo "[lavalink-config] Merged $FRAGMENT_COUNT extension fragment(s)"
else
    echo "[lavalink-config] No extension fragments found — using base config"
fi

echo "[lavalink-config] Generated: $OUTPUT_FILE"
