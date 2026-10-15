#!/bin/bash
# Tests the clone step of sync-extensions.sh with the config.fds switch, using temporary folders.
# It clones from a local git repository, so it needs no network. It needs Docker, because the switch is
# read with the mikefarah/yq image. Exits non-zero if any check fails.
set -u

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
SYNC="$SCRIPT_DIR/sync-extensions.sh"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/plexbot-extensions-test.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

NAME=PlexBot-YouTube-MusicProvider
failures=0

check() {
    local description="$1"
    shift
    if "$@"; then
        echo "PASS: $description"
    else
        echo "FAIL: $description"
        failures=$((failures + 1))
    fi
}

exists() { [ -e "$1" ]; }
absent() { [ ! -e "$1" ]; }
cloned() { [ -f "$1/lavalink.plugin.yml" ]; }
marker_is() { [ -f "$1/marker.txt" ] && [ "$(cat "$1/marker.txt")" = "$2" ]; }
rc_is() { [ "$RC" -eq "$1" ]; }
output_has() { printf '%s\n' "$OUT" | grep -qF -- "$1"; }

# A folder with a marker file, so a kept folder can be told from a cloned one
make_folder() {
    mkdir -p "$1"
    echo "$2" > "$1/marker.txt"
}

new_case() {
    CASE="$WORK/$1"
    EXT="$CASE/Extensions"
    MANIFEST="$CASE/extensions.txt"
    CONFIG="$CASE/config.fds"
    mkdir -p "$EXT"
}

run_clone() {
    OUT="$(EXTENSIONS_DIR="$EXT" EXTENSIONS_MANIFEST="$MANIFEST" CONFIG_FILE="$CONFIG" "$SYNC" clone 2>&1)"
    RC=$?
}

# A local repository with one commit, so a clone has a real working tree to check
SOURCE="$WORK/source"
git init -q "$SOURCE"
echo "# test fragment" > "$SOURCE/lavalink.plugin.yml"
git -C "$SOURCE" add lavalink.plugin.yml
git -C "$SOURCE" -c user.name=test -c user.email=test@example.invalid commit -q -m "Add fragment"
git clone -q --bare "$SOURCE" "$WORK/remote.git"
REMOTE="file://$WORK/remote.git"

new_case true-clones-missing
printf '%s\n' "$REMOTE $NAME youtube.enabled" > "$MANIFEST"
printf '%s\n' "youtube:" "    enabled: true" > "$CONFIG"
run_clone
check "true: exit code is 0" rc_is 0
check "true: a missing folder is cloned" cloned "$EXT/$NAME"
check "true: output says cloning" output_has "is true, cloning"

new_case true-restores-disabled
printf '%s\n' "$REMOTE $NAME youtube.enabled" > "$MANIFEST"
printf '%s\n' "youtube:" "    enabled: true" > "$CONFIG"
make_folder "$EXT/$NAME.disabled" "parked"
run_clone
check "true restores: exit code is 0" rc_is 0
check "true restores: the .disabled folder comes back" marker_is "$EXT/$NAME" "parked"
check "true restores: the .disabled name is gone" absent "$EXT/$NAME.disabled"
check "true restores: nothing is cloned over it" absent "$EXT/$NAME/lavalink.plugin.yml"

new_case false-renames
printf '%s\n' "$REMOTE $NAME youtube.enabled" > "$MANIFEST"
printf '%s\n' "youtube:" "    enabled: false" > "$CONFIG"
make_folder "$EXT/$NAME" "mine"
run_clone
check "false renames: exit code is 0" rc_is 0
check "false renames: the folder becomes .disabled with its contents" marker_is "$EXT/$NAME.disabled" "mine"
check "false renames: the original name is gone" absent "$EXT/$NAME"

new_case false-missing-folder
printf '%s\n' "$REMOTE $NAME youtube.enabled" > "$MANIFEST"
printf '%s\n' "youtube:" "    enabled: false" > "$CONFIG"
run_clone
check "false, no folder: exit code is 0" rc_is 0
check "false, no folder: nothing is cloned" absent "$EXT/$NAME"
check "false, no folder: no .disabled folder is created" absent "$EXT/$NAME.disabled"

new_case key-missing
printf '%s\n' "$REMOTE $NAME youtube.enabled" > "$MANIFEST"
printf '%s\n' "plex:" "    maxConcurrentResolves: 2" > "$CONFIG"
make_folder "$EXT/$NAME" "mine"
run_clone
check "missing key: exit code is 0" rc_is 0
check "missing key: the folder is kept" marker_is "$EXT/$NAME" "mine"
check "missing key: nothing is renamed" absent "$EXT/$NAME.disabled"

new_case key-missing-no-folder
printf '%s\n' "$REMOTE $NAME youtube.enabled" > "$MANIFEST"
printf '%s\n' "plex:" "    maxConcurrentResolves: 2" > "$CONFIG"
run_clone
check "missing key, no folder: the folder is not cloned" absent "$EXT/$NAME"

new_case no-config
printf '%s\n' "$REMOTE $NAME youtube.enabled" > "$MANIFEST"
make_folder "$EXT/$NAME" "mine"
run_clone
check "no config.fds: exit code is 0" rc_is 0
check "no config.fds: the folder is kept" marker_is "$EXT/$NAME" "mine"
check "no config.fds: nothing is renamed" absent "$EXT/$NAME.disabled"

new_case no-config-no-folder
printf '%s\n' "$REMOTE $NAME youtube.enabled" > "$MANIFEST"
run_clone
check "no config.fds, no folder: the folder is not cloned" absent "$EXT/$NAME"

new_case no-key-column
printf '%s\n' "# header comment" "$REMOTE PlexBot-Other-Extension" "$REMOTE $NAME youtube.enabled" "$REMOTE Parked" > "$MANIFEST"
printf '%s\n' "youtube:" "    enabled: false" > "$CONFIG"
make_folder "$EXT/Parked.disabled" "parked"
run_clone
check "no key column: exit code is 0" rc_is 0
check "no key column: a missing folder is still cloned" cloned "$EXT/PlexBot-Other-Extension"
check "no key column: a keyed line with false is not cloned" absent "$EXT/$NAME"
check "no key column: a .disabled folder is left alone" marker_is "$EXT/Parked.disabled" "parked"

new_case both-exist-false
printf '%s\n' "$REMOTE $NAME youtube.enabled" > "$MANIFEST"
printf '%s\n' "youtube:" "    enabled: false" > "$CONFIG"
make_folder "$EXT/$NAME" "active"
make_folder "$EXT/$NAME.disabled" "parked"
run_clone
check "both exist, false: exit code is 0" rc_is 0
check "both exist, false: the folder is untouched" marker_is "$EXT/$NAME" "active"
check "both exist, false: the .disabled folder is untouched" marker_is "$EXT/$NAME.disabled" "parked"
check "both exist, false: a warning is printed" output_has "both exist"

new_case both-exist-true
printf '%s\n' "$REMOTE $NAME youtube.enabled" > "$MANIFEST"
printf '%s\n' "youtube:" "    enabled: true" > "$CONFIG"
make_folder "$EXT/$NAME" "active"
make_folder "$EXT/$NAME.disabled" "parked"
run_clone
check "both exist, true: exit code is 0" rc_is 0
check "both exist, true: the folder is kept" marker_is "$EXT/$NAME" "active"
check "both exist, true: the .disabled folder is kept" marker_is "$EXT/$NAME.disabled" "parked"

new_case unknown-value
printf '%s\n' "$REMOTE $NAME youtube.enabled" > "$MANIFEST"
printf '%s\n' "youtube:" "    enabled: maybe" > "$CONFIG"
make_folder "$EXT/$NAME" "mine"
run_clone
check "unknown value: exit code is 0" rc_is 0
check "unknown value: the folder is kept" marker_is "$EXT/$NAME" "mine"
check "unknown value: output says left as it is" output_has "left as it is"

new_case unreadable-config
printf '%s\n' "$REMOTE $NAME youtube.enabled" > "$MANIFEST"
printf '%s\n' "youtube: [" > "$CONFIG"
make_folder "$EXT/$NAME" "mine"
run_clone
check "unreadable config: exit code is 0" rc_is 0
check "unreadable config: the folder is kept" marker_is "$EXT/$NAME" "mine"

new_case clone-fails
printf '%s\n' "file://$WORK/does-not-exist.git $NAME youtube.enabled" > "$MANIFEST"
printf '%s\n' "youtube:" "    enabled: true" > "$CONFIG"
run_clone
check "failed clone: exit code is 0, the bot still starts" rc_is 0
check "failed clone: a warning is printed" output_has "WARNING: could not clone"
check "failed clone: no folder is left behind" absent "$EXT/$NAME"

new_case shipped-template
printf '%s\n' "$REMOTE $NAME youtube.enabled" > "$MANIFEST"
CONFIG="$ROOT_DIR/RenameMe.config.fds"
run_clone
check "shipped template: exit code is 0" rc_is 0
check "shipped template: reads as false, so a missing folder is not cloned" absent "$EXT/$NAME"
check "shipped template: output says is false" output_has "is false, not present"

echo ""
if [ "$failures" -eq 0 ]; then
    echo "All extension switch checks passed."
    exit 0
fi
echo "$failures extension switch check(s) failed."
exit 1
