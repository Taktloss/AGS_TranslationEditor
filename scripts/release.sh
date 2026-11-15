#!/usr/bin/env bash
# Builds the release archives for all platforms on this machine and optionally uploads them as a
# GitHub release - the same as the "Build and Release" workflow, without using Actions minutes.
#
#   scripts/release.sh 2.8.0            build, test and package into publish/release-2.8.0/
#   scripts/release.sh 2.8.0 --upload   also create the GitHub release v2.8.0 with the archives
#
# The tag v<version> must exist and point at the checked-out commit, and the working tree must be
# clean, so the archives match the tagged sources. Needs the .NET 9 SDK, gcc/make (live hook),
# 7z or zip, and gh for --upload. macOS on Apple Silicon only runs signed executables: with
# rcodesign (cargo install apple-codesign) the osx-arm64 build gets an ad-hoc signature like the
# workflow's macOS runner gives it; without it the osx-arm64 archive is skipped.
set -euo pipefail

version=${1:-}
upload=false
[[ ${2:-} == --upload ]] && upload=true
if [[ ! $version =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$ ]] || [[ $# -gt 2 ]] || { [[ $# -eq 2 ]] && ! $upload; }; then
    echo "Usage: $0 <version, e.g. 2.8.0> [--upload]" >&2
    exit 1
fi
tag=v$version

cd "$(dirname "$0")/.."
root=$PWD

if [[ -n $(git status --porcelain --untracked-files=no) ]]; then
    echo "Error: the working tree has uncommitted changes; commit or stash them first." >&2
    exit 1
fi
if ! git rev-parse -q --verify "refs/tags/$tag" >/dev/null; then
    echo "Error: tag $tag does not exist. Create it first:  git tag -a $tag -m $tag && git push origin $tag" >&2
    exit 1
fi
if [[ $(git rev-parse "$tag^{commit}") != $(git rev-parse HEAD) ]]; then
    echo "Error: $tag is not the checked-out commit; run  git checkout $tag  first." >&2
    exit 1
fi

out=$root/publish/release-$version
if [[ -e $out ]]; then
    echo "Error: $out exists already; move it away or choose another version." >&2
    exit 1
fi
mkdir -p "$out"

if command -v 7z >/dev/null; then zipper=7z; elif command -v zip >/dev/null; then zipper=zip
else echo "Error: neither 7z nor zip is installed." >&2; exit 1; fi

rids=(win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64)
if ! command -v rcodesign >/dev/null; then
    echo "Note: rcodesign not found - skipping osx-arm64 (it needs a signature to start on Apple Silicon)." >&2
    rids=(win-x64 win-arm64 linux-x64 linux-arm64 osx-x64)
fi

echo "== Testing"
dotnet test AGSTools.Tests/AGSTools.Tests.csproj -c Release

echo "== Building the live hook"
make -C AGSLive agslive.so

for rid in "${rids[@]}"; do
    name=AGS_TranslationEditor-$version-$rid
    dir=$out/$name
    echo "== Publishing $rid"
    for project in AGS_TranslationEditor.Avalonia/AGS_TranslationEditor.csproj \
                   AGSFontEditor.App/AGSFontEditor.App.csproj \
                   AGSTranslate/AGSTranslate.csproj; do
        dotnet publish "$project" -c Release -r "$rid" --self-contained true \
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
            -p:DebugType=none -p:Version="$version" -o "$dir"
    done
    cp README.md LICENSE "$dir/"
    cp -r docs "$dir/docs"
    if [[ $rid != win-* ]]; then cp tools/ags-test.sh "$dir/"; fi
    # The live hook patches x86-64 code of the Linux engine
    if [[ $rid == linux-x64 ]]; then cp AGSLive/agslive.so "$dir/"; fi
    if [[ $rid == osx-arm64 ]]; then
        for exe in AGS_TranslationEditor AGSFontEditor.App AGSTranslate; do
            rcodesign sign "$dir/$exe"
        done
    fi
    (
        cd "$out"
        if [[ $rid == win-* ]]; then
            if [[ $zipper == 7z ]]; then 7z a -tzip -bso0 "$name.zip" "$name"; else zip -qr "$name.zip" "$name"; fi
        else
            tar -czf "$name.tar.gz" "$name"
        fi
    )
done

(
    cd "$out"
    sha256sum ./*.zip ./*.tar.gz | sed 's#  \./#  #' > SHA256SUMS.txt
    cat SHA256SUMS.txt
)
assets=("$out"/*.zip "$out"/*.tar.gz "$out"/SHA256SUMS.txt)
echo "== Archives in $out"

if ! $upload; then
    echo "Upload with:  $0 $version --upload  (or gh release create $tag ${assets[*]})"
    exit 0
fi

if gh release view "$tag" >/dev/null 2>&1; then
    echo "Error: the GitHub release $tag exists already; nothing uploaded." >&2
    exit 1
fi
prerelease=()
[[ $version == *-* ]] && prerelease=(--prerelease)
gh release create "$tag" "${assets[@]}" --verify-tag --title "AGS Translation Editor $tag" \
    --notes-file scripts/release-notes.md --generate-notes "${prerelease[@]}"
echo "Released $tag"
