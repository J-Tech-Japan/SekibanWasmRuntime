# Serialized DCB V1 HTTP conformance suite

This directory is a portable consumer artifact. Copy it into a different
repository, provide a fixture for that repository's event/projector/query
names, and run suite.py with a base URL. The suite uses only Python's standard
library and POSTs JSON to the five serialized DCB endpoints. It does not import
an SDK, a runtime assembly, source code, or an implementation helper.

## Fixture

fixture-weather.json is the reference profile used by this repository's
container. A separate implementation supplies the same fields for its own
registered event and query names:

* eventPayloadName and payloadTemplate identify a valid event. The placeholders
  id, tag, and timestamp are replaced for each isolated scenario.
* tagTemplate and tagStateIdTemplate identify a tag and its projector.
* tagGroupTemplate, tagContentTemplate, and tagProjector provide the
  structured identity expected in the tag-state response; they avoid requiring
  a particular separator or tag grammar.
* tagStateLastIdKey is the V1 response key for the tag-state head. The current
  V1 profile is lastSortedUniqueId.
* scalarQuery and listQuery contain queryType and queryParamsJson values that
  the target maps.

## Direct invocation

From the copied directory in the external repository:

~~~sh
python3 suite.py \
  --base-url http://127.0.0.1:8080 \
  --fixture fixture.json \
  --phase before-restart \
  --state-file .artifacts/dcb-v1-state.json \
  --report .artifacts/dcb-v1-before.json
~~~

Restart the target using its own lifecycle tooling, wait for its readiness
endpoint, and run the after-restart phase:

~~~sh
python3 suite.py \
  --base-url http://127.0.0.1:8080 \
  --fixture fixture.json \
  --phase after-restart \
  --state-file .artifacts/dcb-v1-state.json \
  --report .artifacts/dcb-v1-after.json
~~~

The first phase covers unobserved writes, assert-empty success/conflict,
exact-match success/conflict, a multi-tag conflict, reread-and-retry,
rejected null, concurrent SUID allocation, tag-state, and scalar/list query.
The second phase proves that a durable tag head survives a process restart and
that the next SUID is greater. The suite records every HTTP request and
response in its JSON report.

## Negative proof

The portable artifact includes a deliberately broken HTTP forwarding target.
It drops non-empty `consistencyTags` from commit requests. A stale exact-match
commit is therefore accepted by the broken target, and the suite's required
commit-conflict assertion exits non-zero:

~~~sh
python3 broken-tag-proxy.py \
  --upstream http://127.0.0.1:8080 \
  --port 18081 &
python3 suite.py \
  --base-url http://127.0.0.1:18081 \
  --fixture fixture.json \
  --phase broken-tag \
  --state-file .artifacts/dcb-v1-state.json
~~~

The command must run after the normal before/restart phases so the state file
contains a newer `headAfterRestart` than `head`. The expected output contains
`BROKEN_TAG_NEGATIVE=EXPECTED_FAILURE` and the failure detail shows the stale
commit received HTTP 200 instead of a conflict. If the process exits zero, or
the proxy is not used, the negative proof is a failure.
The repository runner performs this lifecycle automatically and records the
non-zero status separately from the passing conformance phases.

## Repository separation

The repository-side runner stages this directory into a fresh temporary Git
repository, runs both phases from that repository's working directory, and
records the two different repository roots. A consumer may use the same
staging pattern or copy this directory into its own repository. The target
implementation is only contacted over HTTP; restarting it is an outer
lifecycle action, not a suite dependency.
