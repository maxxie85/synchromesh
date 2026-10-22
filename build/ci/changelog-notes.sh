#!/usr/bin/env bash
# Prints the CHANGELOG.md section of one version (without its heading): the release notes.
#
#   build/ci/changelog-notes.sh <version> [changelog]     e.g. 0.12.0 or 0.12.0-beta.1 (no leading v)
#
# A release version must have its own "## [<version>] - YYYY-MM-DD" section: the script fails without one, so a tag
# can't be released before the changelog says what's in it. A -dev version uses the [Unreleased] section instead.
# Runs on macOS's Bash 3.2 and any POSIX awk.
set -euo pipefail

VERSION="${1:?usage: changelog-notes.sh <version> [changelog]}"
VERSION="${VERSION#v}"
CHANGELOG="${2:-$(cd "$(dirname "$0")/../.." && pwd)/CHANGELOG.md}"

section() {
    # Lines after the heading "## [$1]" up to the next "## [" heading, without leading and trailing blank lines.
    awk -v name="[$1]" '
        index($0, "## [") == 1 { if (found) exit; found = (index($0, "## " name) == 1); next }
        found { lines[++n] = $0 }
        END {
            if (!found) exit 1
            first = 1; while (first <= n && lines[first] ~ /^[[:space:]]*$/) first++
            last = n;  while (last >= first && lines[last] ~ /^[[:space:]]*$/) last--
            for (i = first; i <= last; i++) print lines[i]
        }' "$CHANGELOG"
}

if NOTES="$(section "$VERSION")"; then
    :
else
    case "$VERSION" in
        (*-dev*)
            NOTES="$(section Unreleased)" || NOTES=""
            ;;
        (*)
            echo "CHANGELOG.md has no section for $VERSION. Add \"## [$VERSION] - $(date +%Y-%m-%d)\" with the changes" \
                 "(move them from [Unreleased]) before tagging." >&2
            exit 1
            ;;
    esac
fi

if [ -z "$NOTES" ]; then
    NOTES="No changes listed."
fi
printf '%s\n' "$NOTES"
