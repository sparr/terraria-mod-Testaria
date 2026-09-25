# shellcheck shell=bash
#
# The paths that live outside this repository, in one place.
#
# Every one of them is an environment variable, so a checkout carries no
# knowledge of where anything sits on a particular machine. The defaults cover
# only the conventional locations; anything else, set the variable.
#
#   TML_PATH         A tModLoader install: the directory holding
#                    tModLoader.dll and tMLMod.targets. Defaults to the
#                    tModLoader directory in the platform's default Steam
#                    library, so an install on another drive, in a second
#                    Steam library folder, or from GOG needs the variable.
#
#   MODS_SRC         Where a mod build deposits its .tmod, which is the Mods
#                    directory under tModLoader's save path. The default
#                    assumes a dev build, because the 1.4.5 line is currently
#                    only available as one; a stable or preview install saves
#                    under tModLoader or tModLoader-preview instead.
#
#   EXAMPLEMOD_SRC   The ExampleMod directory inside a checkout of
#                    https://github.com/tModLoader/tModLoader. Deliberately
#                    has no default: it is a checkout you made rather than
#                    anything an install provides. Only build-examplemod.sh
#                    wants it.
#
# A machine whose layout the defaults do not describe can either export the
# variables, or write them into an untracked scripts/paths.local.sh, which is
# read first if it exists:
#
#   TML_PATH=/mnt/games/SteamLibrary/steamapps/common/tModLoader
#   EXAMPLEMOD_SRC=$HOME/code/tModLoader/ExampleMod
#
# scripts/discover-paths.sh writes that file, and paths.local.sh.example
# beside it is the same thing filled in by hand. The generated file is the
# easier road: it also writes Directory.Build.local.props, which is how the
# same answers reach MSBuild, since a build sees the environment it was
# launched with and not anything sourced here.
#
# Sourced by the other scripts, not run on its own.

# One machine's own answers, if it has any. paths.local.sh is untracked and is
# where a path peculiar to a single computer belongs, so that nothing
# committed here has to know about it. Sourced first, because everything below
# defers to a variable that is already set.
#
# TESTARIA_IGNORE_LOCAL_PATHS skips it, which is what discover-paths.sh needs:
# a script that rewrites that file must not first read the answers out of it,
# or a path that has gone stale can never be corrected.
#
# An if rather than a && chain, because the callers source this file under
# `set -e`: a chain ending in a failed test is the one shape errexit treats as
# an error, so a machine with no paths.local.sh would abort every gate before
# it started.
testaria_local="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/paths.local.sh"
if [ "${TESTARIA_IGNORE_LOCAL_PATHS:-0}" != "1" ] && [ -f "$testaria_local" ]; then
	# shellcheck source=/dev/null
	. "$testaria_local"
fi
unset testaria_local

# First candidate that exists, falling back to the first named so that a
# failure reports the conventional location rather than an empty string.
testaria_first_dir() {
  local candidate
  for candidate in "$@"; do
    if [ -d "$candidate" ]; then
      echo "$candidate"
      return 0
    fi
  done
  echo "$1"
}

# Steam installs games into any number of library folders, on any number of
# drives, and records them in steamapps/libraryfolders.vdf under wherever
# Steam itself lives. Probing a fixed list of paths finds only the primary
# library, so an install on a second drive looks exactly like no install at
# all. Ask Steam instead. Prints one candidate tModLoader directory per
# library, in the order Steam lists them.
testaria_steam_libraries() {
  local root index
  for root in \
    "$HOME/.local/share/Steam" \
    "$HOME/.steam/steam" \
    "$HOME/Library/Application Support/Steam"
  do
    index="$root/steamapps/libraryfolders.vdf"
    [ -r "$index" ] || continue
    # One "path" per library entry, so grabbing that key is unambiguous even
    # though this is not a VDF parser.
    sed -n 's/.*"path"[[:space:]]*"\(.*\)".*/\1/p' "$index"
  done | awk '!seen[$0]++'
}

testaria_tml_candidates() {
  echo "$HOME/.local/share/Steam/steamapps/common/tModLoader"
  echo "$HOME/.steam/steam/steamapps/common/tModLoader"
  echo "$HOME/Library/Application Support/Steam/steamapps/common/tModLoader"
  local library
  while IFS= read -r library; do
    [ -n "$library" ] && echo "$library/steamapps/common/tModLoader"
  done <<EOF
$(testaria_steam_libraries)
EOF
}

# Line by line rather than word by word: "Library/Application Support/Steam"
# has a space in it, and splitting on whitespace would look for two
# directories that do not exist.
testaria_first_dir_from_stdin() {
  local candidate first=""
  while IFS= read -r candidate; do
    [ -n "$candidate" ] || continue
    [ -n "$first" ] || first="$candidate"
    if [ -d "$candidate" ]; then
      echo "$candidate"
      return 0
    fi
  done
  # Falling back to the first named, so a failure reports the conventional
  # location rather than an empty string.
  echo "$first"
}

if [ -z "${TML_PATH:-}" ]; then
  TML_PATH="$(testaria_tml_candidates | testaria_first_dir_from_stdin)"
fi

MODS_SRC="${MODS_SRC:-$(testaria_first_dir \
  "$HOME/.local/share/Terraria/tModLoader-dev/Mods" \
  "$HOME/Library/Application Support/Terraria/tModLoader-dev/Mods")}"

# EXAMPLEMOD_SRC has no default to compute, but is exported all the same so
# that a value from paths.local.sh reaches a script further down the chain.
export TML_PATH MODS_SRC EXAMPLEMOD_SRC
