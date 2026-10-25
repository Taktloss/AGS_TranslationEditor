#!/usr/bin/env bash
# Starts an AGS game from a separate test folder, so translations and fonts can be tried
# without changing the game folder, the player's settings or savegames.
set -euo pipefail

usage() {
    cat <<'EOF'
Usage: ags-test.sh [options] <game folder> [-- <engine options>]

Builds a test folder with links to the game files and starts the game from there, with its
own config, savegames and engine log. The game folder itself is never changed.

Options:
  -t, --tra FILE      test this translation (copied into the test folder and selected)
  -f, --font FILE     use this font instead of the game's file of the same name
                      (can be repeated; a folder adds all agsfnt*.wfn and *.ttf in it)
  -l, --lang NAME     select this translation (default: name of --tra; "" = original texts)
  -d, --debug         engine test mode: Ctrl+D room info, Ctrl+Alt+V runtime info,
                      debug keys of the game script (if the game has any)
  -o, --out FOLDER    test folder (default: ~/.cache/ags-test/<game folder name>)
  -e, --engine FILE   engine to start (default: ags64/ags32 of the game, else 'ags' from PATH)
      --fullscreen    do not force a window
      --software      software renderer (helps with graphics driver problems)
      --mute          no sound
      --fresh         forget savegames and settings of earlier test runs
  -h, --help          show this help

Everything after -- goes to the engine, e.g.  -- --startr 5  or  -- --tell-filepath
EOF
}

die() { echo "ags-test: $*" >&2; exit 1; }

tra='' lang='' lang_set='' debug='' out='' engine='' fullscreen='' software='' mute='' fresh=''
fonts=() extra=() game=''
while (($#)); do
    case $1 in
        -t|--tra) tra=${2:?--tra needs a file}; shift ;;
        -f|--font) fonts+=("${2:?--font needs a file}"); shift ;;
        -l|--lang) lang=${2?--lang needs a name}; lang_set=1; shift ;;
        -d|--debug) debug=1 ;;
        -o|--out) out=${2:?--out needs a folder}; shift ;;
        -e|--engine) engine=${2:?--engine needs a file}; shift ;;
        --fullscreen) fullscreen=1 ;;
        --software) software=1 ;;
        --mute) mute=1 ;;
        --fresh) fresh=1 ;;
        -h|--help) usage; exit 0 ;;
        --) shift; extra=("$@"); break ;;
        -*) die "unknown option $1 (see --help)" ;;
        *) [[ -z $game ]] || die "only one game folder, got '$game' and '$1'"; game=$1 ;;
    esac
    shift
done
[[ -n $game ]] || { usage >&2; exit 1; }
[[ -d $game ]] || die "game folder not found: $game"
game=$(cd "$game" && pwd -P)
[[ -z $tra || -f $tra ]] || die "translation not found: $tra"

out=${out:-${XDG_CACHE_HOME:-$HOME/.cache}/ags-test/$(basename "$game")}
out=$(realpath -m "$out")
case "$out/" in "$game/"*) die "the test folder must not be inside the game folder" ;; esac
case "$game/" in "$out/"*) die "the test folder must not contain the game folder" ;; esac
mkdir -p "$out"
# Only ever delete in folders this script created
if [[ ! -e $out/.ags-test ]]; then
    [[ -z $(ls -A "$out") ]] || die "$out is not empty and was not created by ags-test; choose another --out"
    : > "$out/.ags-test"
fi

# Test copy of the game folder: links to everything, real copies of the files under test
tg=$out/game
rm -rf "$tg"
mkdir -p "$tg" "$out/userdata" "$out/shared"
[[ -z $fresh ]] || rm -rf "${out:?}/userdata/"* "${out:?}/shared/"*
shopt -s nullglob dotglob
for f in "$game"/*; do
    [[ $(basename "$f") == acsetup.cfg ]] || ln -s "$f" "$tg/"
done

replace() { # $1 = file to put into the test folder
    local name
    name=$(basename "$1")
    [[ -e $tg/$name ]] && echo "  replaced: $name" || echo "  added:    $name"
    rm -f "$tg/$name"
    cp "$1" "$tg/$name"
}
for f in "${fonts[@]}"; do
    if [[ -d $f ]]; then
        found=("$f"/agsfnt*.wfn "$f"/*.ttf "$f"/*.TTF)
        ((${#found[@]})) || die "no agsfnt*.wfn or *.ttf in $f"
        for g in "${found[@]}"; do replace "$g"; done
    else
        [[ -f $f ]] || die "font not found: $f"
        replace "$f"
    fi
done
if [[ -n $tra ]]; then
    replace "$tra"
    if [[ -z $lang_set ]]; then lang=$(basename "$tra"); lang=${lang%.*}; lang_set=1; fi
fi
shopt -u nullglob dotglob

# Own config: the game's acsetup.cfg with a few values replaced
set_ini() { # stdin = ini, args = "section<TAB>key<TAB>value" ...
    awk -v ov="$(printf '%s\n' "$@")" '
        BEGIN {
            n = split(ov, lines, "\n")
            for (i = 1; i <= n; i++) {
                if (lines[i] == "") continue
                split(lines[i], p, "\t"); k = tolower(p[1]) "\t" tolower(p[2])
                val[k] = p[3]; name[k] = p[2]; sec[k] = tolower(p[1]); order[++cnt] = k
            }
        }
        function rest(s,   i, k) { # keys of section s that the file did not have
            for (i = 1; i <= cnt; i++) { k = order[i]
                if (sec[k] == s && !(k in done)) { print name[k] "=" val[k]; done[k] = 1 } }
        }
        { sub(/\r$/, "") }
        /^[ \t]*\[/ { rest(cur); cur = tolower($0); gsub(/^[ \t]*\[|\][ \t]*$/, "", cur); print; next }
        /=/ {
            k = $0; sub(/[ \t]*=.*/, "", k); sub(/^[ \t]+/, "", k); k = cur "\t" tolower(k)
            if (k in val) { if (!(k in done)) print name[k] "=" val[k]; done[k] = 1; next }
        }
        { print }
        END {
            rest(cur)
            for (i = 1; i <= cnt; i++) if (!(order[i] in done)) { print "[" sec[order[i]] "]"; rest(sec[order[i]]) }
        }'
}
ini=()
[[ -n $fullscreen ]] || ini+=($'graphics\twindowed\t1')
[[ -z $software ]] || ini+=($'graphics\tdriver\tSoftware')
[[ -z $mute ]] || ini+=($'sound\tenabled\t0')
[[ -z $lang_set ]] || ini+=("language"$'\t'"translation"$'\t'"$lang")
ini+=("misc"$'\t'"user_data_dir"$'\t'"$out/userdata" "misc"$'\t'"shared_data_dir"$'\t'"$out/shared")
{ cat "$game/acsetup.cfg" 2>/dev/null || true; } | set_ini "${ini[@]}" > "$tg/acsetup.cfg"

# Engine: the game's own Linux engine (as its startgame script does), else an installed one
libs=''
if [[ -z $engine ]]; then
    if [[ $(uname -m) == x86_64 && -x $game/ags64 ]]; then engine=$game/ags64; libs=$game/lib64
    elif [[ -x $game/ags32 ]]; then engine=$game/ags32; libs=$game/lib32
    elif command -v ags > /dev/null; then engine=$(command -v ags)
    else die "no engine: the game has no ags64/ags32 and 'ags' is not installed; use --engine"
    fi
fi
[[ -x $engine ]] || die "engine not found or not executable: $engine"

# Engine 3.6.1 ignores --log-file-path and always writes the shared log of all games
engine_log=${XDG_DATA_HOME:-$HOME/.local/share}/ags/ags.log
log=$out/ags.log
rm -f "$log" "$out/output.txt"
args=(--localuserconf --log-file=all:debug)
[[ -z $debug ]] || args+=(--test)
[[ -n $lang_set && -z $lang ]] && args+=(--no-translation)
[[ -n $lang ]] && args+=(--translation "$lang")

echo "Game:        $game"
echo "Test folder: $tg"
if [[ -z $lang_set ]]; then echo "Translation: (as in the game's acsetup.cfg)"
else echo "Translation: ${lang:-(original texts)}"; fi
echo "Engine:      $engine ${args[*]} ${extra[*]}"
echo
start=$(date +%s)
set +e
(
    cd "$tg"
    export LIBGL_DRI3_DISABLE=true
    [[ -z $libs ]] || export ALLEGRO_MODULES=$libs
    exec "$engine" "${args[@]}" "${extra[@]}" "$tg/"
) 2>&1 | tee "$out/output.txt"
status=${PIPESTATUS[0]}
set -e
if [[ -f $engine_log ]] && (($(stat -c %Y "$engine_log") >= start)); then cp "$engine_log" "$log"; fi

echo
echo "Engine exit code: $status"
if [[ -f $log ]]; then
    echo "Log: $log  (terminal output: $out/output.txt)"
    # WFN fonts with unused characters always trigger "mistakes in data format": harmless
    grep -E '^Translation|WARNING|ERROR|Error|FATAL|Abort' "$log" | grep -v 'mistakes in data format' | head -n 25 || true
    errors=$(grep -cE 'ERROR|Error|FATAL' "$log" || true)
    echo "($errors line(s) with errors; full log: less \"$log\")"
fi
exit "$status"
