#!/usr/bin/env bash
#
# Stage and run the portable serialized DCB V1 suite from a separate Git
# repository. The target is contacted by the suite only over HTTP.
#
# Usage:
#   scripts/contract/run-serialized-dcb-v1.sh \
#     --base-url http://127.0.0.1:18080 \
#     --restart-command 'docker compose ... restart runtime'

set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
suite_source="$repo_root/conformance/serialized-dcb-v1"
base_url=""
fixture="$suite_source/fixture-weather.json"
restart_command=""
ready_path="/ready"
ready_timeout=120
report_path="$repo_root/reports/compatibility/serialized-dcb-v1-conformance.md"

usage() {
  sed -n '1,24p' "$0"
}

fail() {
  printf '[dcb-v1] FAIL: %s\n' "$*" >&2
  exit 1
}

while (($# > 0)); do
  case "$1" in
    --base-url)
      [[ $# -ge 2 ]] || fail "--base-url requires a value"
      base_url="$2"
      shift 2
      ;;
    --fixture)
      [[ $# -ge 2 ]] || fail "--fixture requires a value"
      fixture="$2"
      shift 2
      ;;
    --restart-command)
      [[ $# -ge 2 ]] || fail "--restart-command requires a value"
      restart_command="$2"
      shift 2
      ;;
    --ready-path)
      [[ $# -ge 2 ]] || fail "--ready-path requires a value"
      ready_path="$2"
      shift 2
      ;;
    --ready-timeout)
      [[ $# -ge 2 ]] || fail "--ready-timeout requires a value"
      ready_timeout="$2"
      shift 2
      ;;
    --report)
      [[ $# -ge 2 ]] || fail "--report requires a value"
      report_path="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      fail "unknown argument: $1"
      ;;
  esac
done

[[ -n "$base_url" ]] || fail "--base-url is required"
[[ -x "$(command -v python3 || true)" ]] || fail "python3 is required"
[[ -f "$fixture" ]] || fail "fixture not found: $fixture"
[[ -f "$suite_source/suite.py" ]] || fail "suite.py not found: $suite_source"
[[ -n "$restart_command" ]] || fail "--restart-command is required for restart evidence"

external_repo="$(mktemp -d "${TMPDIR:-/tmp}/sekiban-dcb-v1-external.XXXXXX")"
state_file="$external_repo/state.json"
before_report="$external_repo/before.json"
after_report="$external_repo/after.json"
negative_report="$external_repo/negative.json"
before_log="$external_repo/before.log"
after_log="$external_repo/after.log"
negative_log="$external_repo/negative.log"
proxy_log="$external_repo/broken-proxy.log"
proxy_pid=0
cleanup() {
  if ((proxy_pid > 0)); then
    kill "$proxy_pid" >/dev/null 2>&1 || true
    wait "$proxy_pid" >/dev/null 2>&1 || true
  fi
  rm -rf "$external_repo"
}
trap cleanup EXIT

cp "$suite_source/suite.py" "$external_repo/suite.py"
cp "$suite_source/README.md" "$external_repo/README.md"
cp "$suite_source/broken-tag-proxy.py" "$external_repo/broken-tag-proxy.py"
cp "$fixture" "$external_repo/fixture.json"
git -C "$external_repo" init -q
git -C "$external_repo" config user.email "conformance@example.invalid"
git -C "$external_repo" config user.name "Serialized DCB Conformance"
git -C "$external_repo" add suite.py README.md broken-tag-proxy.py fixture.json
git -C "$external_repo" commit -q -m "stage serialized DCB V1 consumer"

source_repo_root="$(git -C "$repo_root" rev-parse --show-toplevel)"
external_repo_root="$(git -C "$external_repo" rev-parse --show-toplevel)"
[[ "$source_repo_root" != "$external_repo_root" ]] || fail "external repository root equals source repository root"

mkdir -p "$(dirname "$report_path")"

run_phase() {
  local phase="$1" report="$2" log="$3"
  printf '[dcb-v1] phase=%s external_repo=%s\n' "$phase" "$external_repo_root"
  (
    cd "$external_repo"
    CONFORMANCE_EXTERNAL_REPOSITORY=true \
      python3 suite.py \
        --base-url "$base_url" \
        --fixture fixture.json \
        --phase "$phase" \
        --state-file state.json \
        --report "$report"
  ) 2>&1 | tee "$log"
}

run_phase before-restart "$before_report" "$before_log"

printf '[dcb-v1] restart command: %s\n' "$restart_command"
bash -lc "$restart_command"

deadline=$((SECONDS + ready_timeout))
ready=0
while ((SECONDS < deadline)); do
  if curl -q --silent --show-error --fail --max-time 5 "${base_url%/}${ready_path}" >/dev/null 2>&1; then
    ready=1
    break
  fi
  sleep 2
done
((ready == 1)) || fail "target did not become ready after restart: ${base_url%/}${ready_path}"

run_phase after-restart "$after_report" "$after_log"

proxy_port="$(python3 -c 'import socket
sock = socket.socket()
sock.bind(("127.0.0.1", 0))
print(sock.getsockname()[1])
sock.close()')"
python3 "$external_repo/broken-tag-proxy.py" \
  --upstream "$base_url" \
  --port "$proxy_port" >"$proxy_log" 2>&1 &
proxy_pid=$!
proxy_url="http://127.0.0.1:${proxy_port}"
proxy_ready=0
proxy_deadline=$((SECONDS + 30))
while ((SECONDS < proxy_deadline)); do
  if curl -q --silent --show-error --fail --max-time 5 "$proxy_url/ready" >/dev/null 2>&1; then
    proxy_ready=1
    break
  fi
  sleep 1
done
((proxy_ready == 1)) || fail "broken-tag proxy did not become ready: $proxy_url/ready"

set +e
(
  cd "$external_repo"
  CONFORMANCE_EXTERNAL_REPOSITORY=true \
    python3 suite.py \
      --base-url "$proxy_url" \
      --fixture fixture.json \
      --phase broken-tag \
      --state-file state.json \
      --report "$negative_report"
) 2>&1 | tee "$negative_log"
  negative_status="${PIPESTATUS[0]}"
set -e
((negative_status != 0)) || fail "deliberately broken tag comparison unexpectedly passed"
grep -q 'BROKEN_TAG_NEGATIVE=EXPECTED_FAILURE' "$negative_log" \
  || fail "negative run did not emit the expected failure marker"
kill "$proxy_pid" >/dev/null 2>&1 || true
wait "$proxy_pid" >/dev/null 2>&1 || true
proxy_pid=0

{
  printf '# Serialized DCB V1 external black-box run\n\n'
  printf -- '- Result: **PASS**\n'
  printf -- '- Source repository root: <source checkout>\n'
  printf -- '- External suite repository root: <fresh temporary Git checkout>\n'
  printf -- '- External invocation proven: **true** (fresh Git repository, different root, suite cwd recorded in JSON; temporary root is removed after the run)\n'
  printf -- '- HTTP-only suite: **true** (Python standard library; no local implementation import)\n'
  printf -- '- Restart lifecycle command executed: **true**\n'
  printf -- '- Deliberately broken tag implementation (HTTP proxy) failed as expected: **true**\n'
  printf -- '- Target: %s\n' "$base_url"
  printf -- '- Suite source: conformance/serialized-dcb-v1/suite.py\n\n'
  printf '## Scenario markers\n\n'
  grep -E '^(BEFORE_RESTART|AFTER_RESTART|CONFORMANCE_RESULT|BROKEN_TAG_NEGATIVE)=' \
    "$before_log" "$after_log" "$negative_log" \
    | sed -E 's#^.*/(before|after|negative)\.log:#\1.log:#; s#^#    #' || true
  printf '\n## Findings boundary\n\n'
  printf 'The normative findings ledger is docs/compatibility/serialized-dcb-v1-findings.md. The run proves the observable scenarios; it does not convert provider partial-write or internal allocator-seed limits into zero findings.\n'
} > "$report_path"

printf '[dcb-v1] PASS report=%s\n' "$report_path"
