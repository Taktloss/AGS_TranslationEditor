#!/usr/bin/env bash
# Runs the AGSTranslate tools over real games and reports what fails, to find games the editor
# does not read correctly yet. Games are only read; everything is written into <outdir>.
#
#   scripts/check-games.sh <AGSTranslate> <outdir> <game file or folder>...
#
# Per game: info, extract, a round trip (every text "translated" with itself, compiled to TRA and
# decompiled again; the number of texts must stay), the check against the game, fonts, sprites
# (with where they are used) and rooms. Writes <outdir>/summary.md and one log per game.
set -u

if [ $# -lt 3 ]; then
    sed -n '2,9p' "$0" | sed 's/^# \{0,1\}//'
    exit 2
fi
tool=$1; out=$2; shift 2
mkdir -p "$out"
summary="$out/summary.md"
{
    echo "| Game | AGS | Encoding | Texts | Round trip | Check | Fonts | Sprites (used) | Rooms | Problems |"
    echo "|---|---|---|---|---|---|---|---|---|---|"
} > "$summary"

# Runs one step with a time limit; logs its output; prints its output; returns its exit code
step() {
    local name=$1; shift
    local result code
    result=$(timeout 900 "$@" 2>&1); code=$?
    printf '\n=== %s (exit %s)\n%s\n' "$name" "$code" "$result" >> "$log"
    printf '%s' "$result"
    return $code
}

for game in "$@"; do
    name=$(basename "$game"); name=${name%.*}
    dir="$out/$name"; mkdir -p "$dir"
    log="$dir/log.txt"; : > "$log"
    problems=()

    info=$(step info "$tool" info "$game") || problems+=("info failed")
    version=$(printf '%s\n' "$info" | sed -n 's/^AGS version: *//p' | head -1)
    encoding=$(printf '%s\n' "$info" | sed -n 's/^Encoding: *//p' | head -1)

    texts="-"
    if extract=$(step extract "$tool" extract "$game" "$dir/texts.trs"); then
        texts=$(printf '%s\n' "$extract" | sed -n 's/^\([0-9]*\) texts.*/\1/p' | head -1)
        printf '%s\n' "$extract" | grep -qi "warn" && problems+=("extract warnings")
    else
        problems+=("extract failed")
    fi

    roundtrip="-"; check="-"
    if [ -s "$dir/texts.trs" ]; then
        # Every text translated with itself, so the TRA contains all of them
        python3 -I - "$dir/texts.trs" "$dir/identity.trs" <<'PY'
import sys
src, dst = sys.argv[1], sys.argv[2]
data = open(src, 'rb').read()
lines = data.split(b'\n')
out, i = [], 0
while i < len(lines):
    line = lines[i].rstrip(b'\r')
    if line.startswith(b'//') or line == b'':
        out.append(line); i += 1; continue
    out.append(line); out.append(line); i += 2
open(dst, 'wb').write(b'\n'.join(out))
PY
        if step trs2tra "$tool" trs2tra "$game" "$dir/identity.trs" "$dir/identity.tra" > /dev/null \
            && step tra2trs "$tool" tra2trs "$dir/identity.tra" "$dir/back.trs" "$game" > /dev/null; then
            # Texts that occur twice are one entry in the TRA; compare unique sources
            u1=$(grep -av "^//" "$dir/identity.trs" | awk 'NR%2==1' | sort -u | grep -c .)
            u2=$(grep -av "^//" "$dir/back.trs" | awk 'NR%2==1' | sort -u | grep -c .)
            roundtrip="$u2/$u1"
            [ "$u1" = "$u2" ] || problems+=("round trip lost texts")
        else
            roundtrip="failed"; problems+=("TRA round trip failed")
        fi
        if c=$(step check "$tool" check "$game" "$dir/identity.trs"); then :; fi
        check=$(printf '%s\n' "$c" | grep -oE '[0-9]+ errors?, [0-9]+ warnings?, [0-9]+ notes?' | tail -1)
        [ -n "$check" ] || { check="failed"; problems+=("check failed"); }
        printf '%s\n' "$c" | grep -qiE "exception|unhandled" && problems+=("check crashed")
    fi

    fonts="-"
    if f=$(step fonts "$tool" fonts "$game"); then
        fonts=$(printf '%s\n' "$f" | grep -cE '^ *[0-9]+ ')
        printf '%s\n' "$f" | grep -qiE "error|could not" && problems+=("font errors")
    else
        problems+=("fonts failed")
    fi

    sprites="-"
    if s=$(step sprites "$tool" sprites "$game" --used); then
        total=$(printf '%s\n' "$s" | sed -n 's/^\([0-9]*\) sprites.*/\1/p' | head -1)
        used=$(printf '%s\n' "$s" | grep -cE '^ *[0-9]+ +[0-9]+x')
        sprites="$total ($used)"
    else
        sprites="no"
        printf '%s\n' "$s" | grep -qiE "exception|unhandled" && problems+=("sprites crashed")
    fi

    rooms="-"
    if r=$(step rooms "$tool" rooms "$game"); then
        rooms=$(printf '%s\n' "$r" | sed -n 's/^\([0-9]*\) rooms.*/\1/p' | head -1)
    else
        rooms="no"
        printf '%s\n' "$r" | grep -qiE "exception|unhandled" && problems+=("rooms crashed")
    fi

    grep -qiE "unhandled exception|   at [A-Z]" "$log" && problems+=("exception (see log)")
    p=$(IFS=';'; echo "${problems[*]:-}"); p=${p//;/; }
    echo "| $name | ${version:--} | ${encoding:--} | ${texts:--} | $roundtrip | ${check:--} | $fonts | $sprites | $rooms | ${p:-ok} |" >> "$summary"
    echo "$name: ${p:-ok}"
done
echo
cat "$summary"
