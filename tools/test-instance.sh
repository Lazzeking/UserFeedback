#!/usr/bin/env bash
# Starts Vintage Story with its own data folder and only this mod, its example mod and the mods you name,
# to check a problem report without touching your normal mods, settings or saves.
#
#   tools/test-instance.sh <instance> [mod ...] [--fresh] [--no-mod] [--creative]
#
#   instance    name of the test folder, for example insanemonkey
#   mod         a mod id (overhaullib), optionally with a version (ardynleveling@1.0.17), or a path to a mod zip or
#               folder. A mod id comes from your normal Mods folder, or is downloaded from the Mod DB when it is not
#               there: the version you asked for, else the newest release for your game version. Dependencies too.
#   --fresh     delete the world this run opens first, the other one stays
#   --no-mod    leave this mod and its example out, to see whether a problem happens without them
#   --creative  a creative flat world instead, its own one next to the survival one
#
# Example: tools/test-instance.sh insanemonkey overhaullib ardynleveling@1.0.17
#
# The instance lives inside the Flatpak's own folder, because the Flatpak cannot read anywhere else. Its logs are in
# <instance>/Logs. The first start copies your clientsettings.json so you stay logged in.

set -euo pipefail

# --- This mod ---------------------------------------------------------------------------------------------------------

SCRIPT_DIR="$(dirname "$(realpath "$0")")"
PROJECT_FILE="$SCRIPT_DIR/../UserFeedback.csproj"
BUILD_CONFIG=ReleaseToModsWithExample
# The mods the build installs, comma separated, so they are never looked up as dependencies.
OWN_MOD_IDS=userfeedback,userfeedbackexample
# --no-<this id> works like --no-mod.
MAIN_MOD_ID=userfeedback
SURVIVAL_WORLD=userfeedback-test
CREATIVE_WORLD=userfeedback-creative

# --- Shared by every mod's copy of this script ------------------------------------------------------------------------

APP_ID=at.vintagestory.VintageStory
APP_DATA="$HOME/.var/app/$APP_ID"
MAIN_DATA="$APP_DATA/config/VintagestoryData"
INSTANCES="$APP_DATA/test-instances"
DOWNLOADS="$INSTANCES/.moddb"

# The survival preset, the same one the official mod template opens its test world with
PLAY_STYLE=preset-surviveandbuild
WORLD=$SURVIVAL_WORLD

# Filled in by parse_args and resolve_mods
NAME=""
INSTANCE=""
FRESH=false
OWN_MODS=true
MODS=()
SOURCES=()

# Prints the comment block at the top of this file, whatever its length, and stops
usage() {
    sed -n '2,/^[^#]/p' "$0" | sed '$d' | sed 's/^# \{0,1\}//'
    exit 1
}

parse_args() {
    if [[ $# -lt 1 || $1 == -* ]]; then
        usage
    fi

    NAME=$1
    INSTANCE="$INSTANCES/$NAME"
    shift

    for arg in "$@"; do
        case $arg in
            --fresh) FRESH=true ;;
            # --no-<modid> is the older name, kept so it keeps working
            --no-mod | "--no-$MAIN_MOD_ID") OWN_MODS=false ;;
            --creative)
                # Vanilla's creative building play style: a flat world in creative mode, its own one next to the
                # survival one
                WORLD=$CREATIVE_WORLD
                PLAY_STYLE=creativebuilding
                ;;
            -*)
                echo "Unknown option: $arg" >&2
                usage
                ;;
            *) MODS+=("$arg") ;;
        esac
    done
}

# Works out every named mod and its dependencies before anything changes, so a typo or a missing mod stops the run.
# The output is captured first: under set -e a failing command substitution stops the script, a process substitution
# would not. MOD_IDS tells it which mods the build installs, so they are never looked up as dependencies.
resolve_mods() {
    if [[ ${#MODS[@]} -eq 0 ]]; then
        return
    fi

    local resolved
    resolved=$(MOD_IDS="$OWN_MOD_IDS" python3 "$SCRIPT_DIR/resolve_mods.py" "$MAIN_DATA/Mods" "$DOWNLOADS" "${MODS[@]}")
    mapfile -t SOURCES <<< "$resolved"
}

# A fresh Mods folder each run, and with --fresh a new copy of the world this run opens; the other world stays. The
# settings are copied once, to stay logged in.
prepare_instance() {
    mkdir -p "$INSTANCE"
    rm -rf "$INSTANCE/Mods"
    mkdir -p "$INSTANCE/Mods"

    if $FRESH; then
        rm -f "$INSTANCE/Saves/$WORLD.vcdbs"
    fi

    if [[ ! -e $INSTANCE/clientsettings.json && -e $MAIN_DATA/clientsettings.json ]]; then
        cp "$MAIN_DATA/clientsettings.json" "$INSTANCE/"
    fi
}

# The settings list the folders mods load from, and copied ones point at your normal Mods folder. This keeps the game's
# own "Mods" folder, which holds the vanilla mods, and swaps the rest for this instance's Mods folder.
use_instance_mods_folder() {
    local settings="$INSTANCE/clientsettings.json"
    if [[ ! -e $settings ]]; then
        return
    fi

    python3 - "$settings" "$INSTANCE/Mods" <<'EOF'
import json
import sys

path, mods = sys.argv[1], sys.argv[2]
with open(path) as file:
    settings = json.load(file)
settings.setdefault("stringListSettings", {})["modPaths"] = ["Mods", mods]
with open(path, "w") as file:
    json.dump(settings, file, indent=2)
EOF
}

# The build's own packaging step installs this repository's mods into the folder it is given
install_own_mods() {
    if $OWN_MODS; then
        dotnet build "$PROJECT_FILE" -c "$BUILD_CONFIG" -p:ModsFolder="$INSTANCE/Mods" -nologo -v:q
    fi
}

install_named_mods() {
    local source
    for source in "${SOURCES[@]}"; do
        cp -r "$source" "$INSTANCE/Mods/"
    done
}

start_game() {
    echo "Mods in $NAME:"
    ls -1 "$INSTANCE/Mods" | sed 's/^/  /'
    echo "World: $WORLD"
    echo "Logs: $INSTANCE/Logs"

    flatpak run "$APP_ID" --dataPath "$INSTANCE" --openWorld "$WORLD" --playStyle "$PLAY_STYLE" --tracelog
}

main() {
    parse_args "$@"
    resolve_mods
    prepare_instance
    use_instance_mods_folder
    install_own_mods
    install_named_mods
    start_game
}

main "$@"
