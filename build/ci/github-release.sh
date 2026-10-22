#!/usr/bin/env bash
# Creates (or reuses) the GitHub release for a tag and uploads files to it, with the GitHub CLI (on every GitHub
# runner).
#
#   build/ci/github-release.sh <tag> <file>...
#
# Needs GH_TOKEN (the workflow passes the Actions token, with contents: write) and GH_REPO (owner/name). A tag with a
# suffix (v1.2.0-beta.1) becomes a pre-release. The notes come from release-notes.sh: the version's CHANGELOG.md
# section, then the install instructions. Re-running replaces assets with the same name, so a failed release job can
# simply be run again.
set -euo pipefail

TAG="${1:?usage: github-release.sh <tag> <file>...}"
shift
: "${GH_TOKEN:?}" "${GH_REPO:?}"

VERSION="${TAG#v}"
PRERELEASE=""
case "$VERSION" in *-*) PRERELEASE="--prerelease" ;; esac

NOTES="$(mktemp)"
bash "$(dirname "$0")/release-notes.sh" "$VERSION" > "$NOTES"

if gh release view "$TAG" > /dev/null 2>&1; then
    echo "Using the existing release for $TAG"
else
    gh release create "$TAG" --verify-tag --title "Synchromesh $VERSION" --notes-file "$NOTES" $PRERELEASE
    echo "Created the release for $TAG"
fi

gh release upload "$TAG" "$@" --clobber
for FILE in "$@"; do echo "Uploaded $(basename "$FILE")"; done
