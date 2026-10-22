#!/usr/bin/env bash
# Creates (or reuses) the Gitea release for a tag and uploads files to it.
#
#   build/ci/gitea-release.sh <tag> <file>...
#
# Needs GITEA_SERVER (https://git.example), GITEA_REPOSITORY (owner/name) and GITEA_TOKEN in the environment;
# the workflow passes the Actions token. A tag with a suffix (v1.2.0-beta.1) becomes a pre-release. The notes come from
# release-notes.sh: the version's CHANGELOG.md section, then the install instructions.
# Re-running replaces assets with the same name, so a failed release job can simply be run again.
set -euo pipefail

TAG="${1:?usage: gitea-release.sh <tag> <file>...}"
shift
: "${GITEA_SERVER:?}" "${GITEA_REPOSITORY:?}" "${GITEA_TOKEN:?}"

API="$GITEA_SERVER/api/v1/repos/$GITEA_REPOSITORY"
AUTH="Authorization: token $GITEA_TOKEN"
VERSION="${TAG#v}"
PRERELEASE=false
case "$VERSION" in *-*) PRERELEASE=true ;; esac

json() { python3 -c "import json, sys; print(json.load(sys.stdin)$1)"; }

NOTES="$(bash "$(dirname "$0")/release-notes.sh" "$VERSION")"

STATUS=$(curl -s -o /tmp/release.json -w "%{http_code}" -H "$AUTH" "$API/releases/tags/$TAG")
if [ "$STATUS" = "200" ]; then
    RELEASE_ID=$(json "['id']" < /tmp/release.json)
    echo "Using the existing release $RELEASE_ID for $TAG"
else
    BODY=$(python3 -c 'import json, sys; print(json.dumps({"tag_name": sys.argv[1], "name": "Synchromesh " + sys.argv[2],
        "body": sys.argv[3], "draft": False, "prerelease": sys.argv[4] == "true"}))' "$TAG" "$VERSION" "$NOTES" "$PRERELEASE")
    RELEASE_ID=$(curl -fsS -X POST -H "$AUTH" -H "Content-Type: application/json" -d "$BODY" "$API/releases" | json "['id']")
    echo "Created release $RELEASE_ID for $TAG"
fi

for FILE in "$@"; do
    NAME="$(basename "$FILE")"
    EXISTING=$(curl -fsS -H "$AUTH" "$API/releases/$RELEASE_ID/assets" \
        | python3 -c 'import json, sys; print(next((str(a["id"]) for a in json.load(sys.stdin) if a["name"] == sys.argv[1]), ""))' "$NAME")
    if [ -n "$EXISTING" ]; then
        curl -fsS -X DELETE -H "$AUTH" "$API/releases/$RELEASE_ID/assets/$EXISTING" > /dev/null
    fi
    curl -fsS -X POST -H "$AUTH" -F "attachment=@$FILE" \
        "$API/releases/$RELEASE_ID/assets?name=$(python3 -c 'import sys, urllib.parse; print(urllib.parse.quote(sys.argv[1]))' "$NAME")" > /dev/null
    echo "Uploaded $NAME"
done
