#!/usr/bin/env python3
"""Works out the mod files a test instance needs, for tools/test-instance.sh.

Each mod is a path to a zip or folder, or a mod id with an optional version (ardynleveling, ardynleveling@1.0.17).
A mod id is taken from your normal Mods folder when it is there, and downloaded from the Mod DB when it is not.
Dependencies are added the same way. Prints one path per line; says what it picked and why on stderr.

    resolve_mods.py <normal Mods folder> <download folder> [mod ...]
"""

import glob
import json
import os
import re
import sys
import urllib.error
import urllib.request
import zipfile
from dataclasses import dataclass, field
from typing import Optional


# ---------------------------------------------------------------------------------------------------------------------
# Settings

MOD_DB_API = "https://mods.vintagestory.at/api/mod/"
MOD_DB_TIMEOUT_SECONDS = 30
DOWNLOAD_CHUNK_BYTES = 64 * 1024

# Dependencies that are never looked up: the game itself, and the mods tools/test-instance.sh builds from its own
# repository, which it passes in MOD_IDS, comma separated.
BUILTIN_MOD_IDS = {"game", "survival", "creative"} | {modid for modid in os.environ.get("MOD_IDS", "").lower().split(",") if modid}

# Where the game's version file is looked for. VINTAGE_STORY points at the install, as for the build; otherwise the
# system and user Flatpak installs are tried.
GAME_INSTALLS = [path for path in [
    os.environ.get("VINTAGE_STORY"),
    "/var/lib/flatpak/app/at.vintagestory.VintageStory/current/active/files/extra/vintagestory",
    os.path.expanduser("~/.local/share/flatpak/app/at.vintagestory.VintageStory/current/active/files/extra/vintagestory"),
] if path]


# ---------------------------------------------------------------------------------------------------------------------
# Messages. Paths go to stdout for the shell script; everything said to the person goes to stderr.

def say(message: str) -> None:
    sys.stderr.write(message + "\n")


def fail(message: str) -> None:
    say(message)
    sys.exit(1)


# ---------------------------------------------------------------------------------------------------------------------
# Reading mods

@dataclass(frozen=True)
class ModInfo:
    path: str
    modid: str
    version: Optional[str]
    dependencies: dict = field(default_factory=dict)


def read_modinfo_text(path: str) -> Optional[str]:
    """The modinfo.json of a mod zip or folder, or None when there is none."""
    try:
        if os.path.isdir(path):
            with open(os.path.join(path, "modinfo.json"), encoding="utf-8-sig") as file:
                return file.read()
        with zipfile.ZipFile(path) as archive:
            return archive.read("modinfo.json").decode("utf-8-sig")
    except (OSError, KeyError, zipfile.BadZipFile):
        return None


def read_modinfo(path: str) -> Optional[ModInfo]:
    """The mod id, version and dependencies of a mod zip or folder, or None when it is not a mod.

    modinfo.json is often loose JSON with comments or trailing commas, so only the needed fields are read, by pattern.
    """
    text = read_modinfo_text(path)
    modid = text and re.search(r'"modid"\s*:\s*"([^"]+)"', text, re.I)
    if not modid:
        return None

    version = re.search(r'"version"\s*:\s*"([^"]+)"', text, re.I)
    dependencies = re.search(r'"dependencies"\s*:\s*\{([^}]*)\}', text, re.I | re.S)
    return ModInfo(
        path=path,
        modid=modid.group(1).lower(),
        version=version.group(1) if version else None,
        dependencies=dict(re.findall(r'"([^"]+)"\s*:\s*"([^"]*)"', dependencies.group(1))) if dependencies else {},
    )


def mods_in(folder: str) -> dict:
    """Every mod in a folder, by mod id. One id can have several copies, in different versions."""
    found = {}
    if not os.path.isdir(folder):
        return found

    for name in sorted(os.listdir(folder)):
        mod = read_modinfo(os.path.join(folder, name))
        if mod:
            found.setdefault(mod.modid, []).append(mod)
    return found


# ---------------------------------------------------------------------------------------------------------------------
# Versions

def version_key(version: Optional[str]) -> tuple:
    """Sorts versions the way semantic versioning does: part by part, a number part above a text part such as rc, and
    a pre-release (the part after -) below the release itself, so 1.0.0-pre.1 < 1.0.0 < 1.0.1. Numbers and text are
    never compared with each other."""
    def parts(text: str) -> list:
        return [(1, int(part), "") if part.isdigit() else (0, 0, part) for part in re.split(r"[.+]", text) if part]

    release, _, pre = (version or "").partition("-")
    return parts(release), 0 if pre else 1, parts(pre)


def game_version() -> Optional[str]:
    """The installed game's version, such as 1.22.7, from its assets/version-<version>.txt."""
    for install in GAME_INSTALLS:
        for path in glob.glob(os.path.join(install, "assets", "version-*.txt")):
            return os.path.basename(path)[len("version-"):-len(".txt")]
    return None


# ---------------------------------------------------------------------------------------------------------------------
# Mod DB

def fetch_releases(modid: str) -> list:
    """The mod's releases on the Mod DB, newest first."""
    try:
        with urllib.request.urlopen(MOD_DB_API + modid, timeout=MOD_DB_TIMEOUT_SECONDS) as response:
            data = json.load(response)
    except (urllib.error.URLError, json.JSONDecodeError) as error:
        fail(f"Could not ask the Mod DB about '{modid}': {error}")

    mod = data.get("mod") if str(data.get("statuscode")) == "200" else None
    if not mod:
        fail(f"'{modid}' is not in your Mods folder and the Mod DB does not know it")
    return mod.get("releases") or []


def pick_release(releases: list, version: Optional[str], game: Optional[str]) -> Optional[dict]:
    """The version asked for; otherwise the newest release tagged for this game version, then for its minor version
    (1.22.x), then the newest release of all."""
    if version:
        return next((release for release in releases if release.get("modversion") == version), None)

    def tags(release: dict) -> list:
        return release.get("tags") or []

    if game:
        minor = ".".join(game.split(".")[:2])
        for matches in (lambda tag: tag == game, lambda tag: tag.startswith(minor)):
            release = next((release for release in releases if any(matches(tag) for tag in tags(release))), None)
            if release:
                return release
    return releases[0] if releases else None


def download_release(modid: str, release: dict, folder: str) -> str:
    """The release's file in the download folder, downloaded unless an earlier run already has it."""
    # Only the name is used, so a file name from the Mod DB cannot point outside the download folder.
    filename = os.path.basename(release.get("filename") or "")
    if not filename:
        fail(f"The Mod DB gave no file name for '{modid}' {release.get('modversion', '')}")

    os.makedirs(folder, exist_ok=True)
    path = os.path.join(folder, filename)
    if os.path.exists(path):
        return path

    # Written to a .part file first, so a download that stops halfway never looks like a finished mod.
    say(f"Downloading {filename} from the Mod DB")
    try:
        with urllib.request.urlopen(release["mainfile"], timeout=MOD_DB_TIMEOUT_SECONDS) as response, \
                open(path + ".part", "wb") as file:
            while chunk := response.read(DOWNLOAD_CHUNK_BYTES):
                file.write(chunk)
    except (urllib.error.URLError, OSError) as error:
        fail(f"Could not download {filename}: {error}")
    os.replace(path + ".part", path)
    return path


def download(modid: str, version: Optional[str], folder: str, game: Optional[str]) -> str:
    releases = fetch_releases(modid)
    release = pick_release(releases, version, game)
    if not release:
        wanted = f"version {version}" if version else "a release"
        known = ", ".join(release.get("modversion", "?") for release in releases[:10])
        fail(f"The Mod DB has no {wanted} of '{modid}'. Versions there: {known}")
    return download_release(modid, release, folder)


# ---------------------------------------------------------------------------------------------------------------------
# Resolving

class Resolver:
    """Finds mods by path or id: in your normal Mods folder, then in earlier downloads, then on the Mod DB."""

    def __init__(self, mods_folder: str, download_folder: str):
        self.download_folder = download_folder
        self.sources = [("your Mods folder", mods_in(mods_folder)), ("earlier downloads", mods_in(download_folder))]
        self.game = game_version()

    def find(self, spec: str) -> str:
        """The path for one mod: a path as it is, or a mod id, optionally with @version."""
        if os.path.exists(spec):
            return os.path.realpath(spec)

        modid, _, version = spec.partition("@")
        modid = modid.lower()
        for source, mods in self.sources:
            copies = [mod for mod in mods.get(modid, []) if not version or mod.version == version]
            if copies:
                newest = max(copies, key=lambda mod: version_key(mod.version))
                say(f"{modid} {newest.version} from {source}")
                return newest.path

        path = download(modid, version or None, self.download_folder, self.game)
        downloaded = read_modinfo(path)
        say(f"{modid} {downloaded.version if downloaded else '?'} from the Mod DB")
        return path

    def with_dependencies(self, specs: list) -> list:
        """The paths of the mods asked for and of everything they depend on, each once, in the order found."""
        result, seen = [], set()
        queue = [self.find(spec) for spec in specs]
        while queue:
            path = queue.pop(0)
            if path in seen:
                continue
            seen.add(path)
            result.append(path)

            mod = read_modinfo(path)
            for dependency in (mod.dependencies if mod else {}):
                dependency = dependency.lower()
                if dependency not in BUILTIN_MOD_IDS and dependency != (mod.modid if mod else None):
                    # A dependency's version is only a minimum, so any copy that is there will do.
                    queue.append(self.find(dependency))
        return result


def main(argv: list) -> None:
    if len(argv) < 2:
        fail(__doc__)

    mods_folder, download_folder, specs = argv[0], argv[1], argv[2:]
    print("\n".join(Resolver(mods_folder, download_folder).with_dependencies(specs)))


if __name__ == "__main__":
    main(sys.argv[1:])
