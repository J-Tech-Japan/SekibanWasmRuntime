#!/usr/bin/env bash
set -euo pipefail

# Smoke-test write path for the Go DCB sample. Posts three SerializableEventCandidate payloads
# (ClassRoomCreated, StudentCreated, StudentEnrolledInClassRoom) directly at wasmserver's
# /api/sekiban/serialized/commit so the MV catch-up worker picks them up, then polls the Go
# ClientApi's /api/mv/enrollments to confirm the projection arrived. Run once the Aspire
# AppHost is up.
#
# Usage:
#   ./build/scripts/seed-go-mv.sh
#   WASM_SERVER=http://127.0.0.1:7199 CLIENT_API=http://127.0.0.1:7198 ./build/scripts/seed-go-mv.sh

WASM_SERVER="${WASM_SERVER:-http://127.0.0.1:7199}"
CLIENT_API="${CLIENT_API:-http://127.0.0.1:7198}"
CURL="${CURL:-/usr/bin/curl}"

if command -v uuidgen >/dev/null 2>&1; then
  class_room_id=$(uuidgen | tr 'A-Z' 'a-z')
  student_id=$(uuidgen | tr 'A-Z' 'a-z')
else
  class_room_id=$(python3 -c 'import uuid;print(uuid.uuid4())')
  student_id=$(python3 -c 'import uuid;print(uuid.uuid4())')
fi

echo "[seed] wasmserver=$WASM_SERVER"
echo "[seed] clientapi=$CLIENT_API"
echo "[seed] classroom=$class_room_id"
echo "[seed] student=$student_id"

# Verify wasmserver stays generic — no /api/mv/*.
mv_status_on_wasm=$($CURL --no-fail -s -o /dev/null -w '%{http_code}' "$WASM_SERVER/api/mv/status" || true)
if [[ "$mv_status_on_wasm" != "404" ]]; then
  echo "[seed] WARN wasmserver/api/mv/status returned $mv_status_on_wasm (expected 404)"
fi

commit_candidate() {
  local event_type="$1" payload_json="$2" tag="$3"
  local payload_b64
  payload_b64=$(printf '%s' "$payload_json" | base64 | tr -d '\n')
  local body
  body=$(cat <<JSON
{
  "eventCandidates": [
    { "payload": "$payload_b64", "eventPayloadName": "$event_type", "tags": ["$tag"] }
  ],
  "consistencyTags": []
}
JSON
  )
  $CURL --no-fail -s -X POST -H 'Content-Type: application/json' \
    -d "$body" "$WASM_SERVER/api/sekiban/serialized/commit"
}

echo "[seed] committing ClassRoomCreated"
commit_candidate "ClassRoomCreated" \
  "{\"classRoomId\":\"$class_room_id\",\"name\":\"Go MV Class\",\"maxStudents\":10}" \
  "ClassRoom:$class_room_id"
echo
echo "[seed] committing StudentCreated"
commit_candidate "StudentCreated" \
  "{\"studentId\":\"$student_id\",\"name\":\"Go MV Student\",\"maxClassCount\":5}" \
  "Student:$student_id"
echo
echo "[seed] committing StudentEnrolledInClassRoom"
commit_candidate "StudentEnrolledInClassRoom" \
  "{\"studentId\":\"$student_id\",\"classRoomId\":\"$class_room_id\"}" \
  "ClassRoom:$class_room_id"
echo

echo "[seed] polling ClientApi /api/mv/enrollments..."
for i in $(seq 1 30); do
  sleep 1
  mv_resp=$($CURL --no-fail -s "$CLIENT_API/api/mv/enrollments?student_id=$student_id" || true)
  if [[ -n "$mv_resp" && "$mv_resp" != "[]" ]]; then
    echo "[seed] OK $mv_resp"
    exit 0
  fi
done

echo "[seed] FAIL — MV did not pick up the enrollment within 30s"
exit 1
