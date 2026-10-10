#!/bin/bash
# Keeps the extensions and the Lavalink plugins they need in place. Install/start.sh runs it twice:
#   sync-extensions.sh clone     clone each extension in Install/extensions.txt whose folder is missing
#   sync-extensions.sh plugins   download each Lavalink plugin in the generated config that is missing from Install/Docker/plugins
# Lavalink runs as its own user and cannot write to Install/Docker/plugins, so the jars are fetched here, as you.
# The environment variables only exist so the script can be tested against other folders.
set -eu

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
MANIFEST="${EXTENSIONS_MANIFEST:-$SCRIPT_DIR/extensions.txt}"
EXTENSIONS_DIR="${EXTENSIONS_DIR:-$ROOT_DIR/Extensions}"
LAVALINK_CONFIG="${LAVALINK_CONFIG:-$SCRIPT_DIR/Docker/lavalink.application.yml}"
PLUGINS_DIR="${PLUGINS_DIR:-$SCRIPT_DIR/Docker/plugins}"
RELEASES_REPO="https://maven.lavalink.dev/releases"

clone_extensions() {
    if [ ! -f "$MANIFEST" ]; then
        echo "[extensions] no manifest at $MANIFEST, nothing to clone"
        return 0
    fi
    mkdir -p "$EXTENSIONS_DIR"
    while read -r url name _; do
        case "$url" in
            ''|'#'*) continue ;;
        esac
        if [ -z "${name:-}" ]; then
            echo "[extensions] skipped '$url': the manifest line needs a folder name"
            continue
        fi
        target="$EXTENSIONS_DIR/$name"
        if [ -e "$target" ]; then
            echo "[extensions] $name: already present"
        elif git clone --quiet "$url" "$target" </dev/null; then
            echo "[extensions] $name: cloned from $url"
        else
            # An extension that cannot be cloned only removes its feature; the bot still starts
            echo "[extensions] WARNING: could not clone $name from $url. The bot starts without it."
        fi
    done < "$MANIFEST"
}

fetch_plugins() {
    if [ ! -f "$LAVALINK_CONFIG" ]; then
        echo "[plugins] no generated Lavalink config at $LAVALINK_CONFIG, nothing to fetch"
        return 0
    fi
    mkdir -p "$PLUGINS_DIR"
    present=0
    fetched=0
    failed=0
    # Each plugin is a "- dependency:" item in the generated config, for example dev.lavalink.youtube:youtube-plugin:1.18.2
    while IFS= read -r dependency; do
        IFS=: read -r group artifact version <<< "$dependency"
        if [ -z "${version:-}" ]; then
            echo "[plugins] WARNING: cannot read the plugin '$dependency'"
            failed=$((failed + 1))
            continue
        fi
        jar="$artifact-$version.jar"
        if [ -f "$PLUGINS_DIR/$jar" ]; then
            echo "[plugins] $jar: present"
            present=$((present + 1))
            continue
        fi
        url="$RELEASES_REPO/$(echo "$group" | tr . /)/$artifact/$version/$jar"
        partial="$PLUGINS_DIR/.$jar.part"
        echo "[plugins] downloading $jar"
        if curl -fSL --retry 2 -m 300 -o "$partial" "$url" && mv "$partial" "$PLUGINS_DIR/$jar" && chmod 644 "$PLUGINS_DIR/$jar"; then
            fetched=$((fetched + 1))
        else
            rm -f "$partial"
            echo "[plugins] FAILED to download $jar from $url"
            failed=$((failed + 1))
        fi
    done < <(awk '/^[[:space:]]*- dependency:/ { value = $0; sub(/.*dependency:[[:space:]]*/, "", value); gsub(/["\047 ]/, "", value); print value }' "$LAVALINK_CONFIG")

    echo "[plugins] $present present, $fetched downloaded, $failed failed"
    if [ "$failed" -gt 0 ]; then
        # Lavalink would try the same download itself and fail, which stops every player, so stop here with the reason
        echo "[plugins] Lavalink cannot start without these plugins. Fix the network or the version, then run Install/start.sh again."
        exit 1
    fi
}

case "${1:-}" in
    clone) clone_extensions ;;
    plugins) fetch_plugins ;;
    *)
        echo "usage: $0 clone|plugins" >&2
        exit 2
        ;;
esac
