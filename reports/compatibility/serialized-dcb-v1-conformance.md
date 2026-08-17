# Serialized DCB V1 external black-box run

- Result: **PASS**
- Source repository root: <source checkout>
- External suite repository root: <fresh temporary Git checkout>
- External invocation proven: **true** (fresh Git repository, different root, suite cwd recorded in JSON; temporary root is removed after the run)
- HTTP-only suite: **true** (Python standard library; no local implementation import)
- Restart lifecycle command executed: **true**
- Deliberately broken commit tag comparison (HTTP proxy) failed as expected: **true**
- Target: http://127.0.0.1:57280
- Suite source: conformance/serialized-dcb-v1/suite.py

## Scenario markers

    before.log:BEFORE_RESTART=PASS tag=weather:g082-a517acade5c2 head=063922597570096092200125817952
    before.log:CONFORMANCE_RESULT=PASS phase=before-restart requests=32
    after.log:AFTER_RESTART=PASS tag=weather:g082-a517acade5c2 old=063922597570096092200125817952 new=063922597574471579201485603951
    after.log:CONFORMANCE_RESULT=PASS phase=after-restart requests=6
    negative.log:CONFORMANCE_RESULT=FAIL detail=broken-commit-exact-conflict: expected conflict HTTP 400/409, got 200: {'writtenEvents': [{'payload': 'eyJmb3JlY2FzdElkIjoiZzA4Mi1hNTE3YWNhZGU1YzItYnJva2VuLWNvbW1pdCIsImxvY2F0aW9uIjoiVG9reW8iLCJ0ZW1wZXJhdHVyZUMiOjIxLCJzdW1tYXJ5IjoiU1dSLUcwODIiLCJjcmVhdGVkQXQiOiIyMDI2LTA4LTE3VDIxOjA2OjE3LjIwNloifQ==', 'sortableUniqueIdValue': '063922597577217464900082002382', 'id': '01a0118b-ea01-7f72-9ede-b3f36a0fb2f4', 'eventMetadata': {'causationId': '01a0118b-ea01-7f72-9ede-b3f36a0fb2f4', 'correlationId': 'SerializedCommit', 'executedUser': 'SerializedSekibanExecutor'}, 'tags': ['weather:g082-a517acade5c2'], 'eventPayloadName': 'WeatherForecastCreated'}], 'tagWriteResults': [{'tag': 'weather:g082-a517acade5c2', 'version': 1, 'writtenAt': '2026-08-17T21:06:17.2199331+00:00'}], 'duration': '00:00:00.0103440'}
    negative.log:BROKEN_TAG_NEGATIVE=EXPECTED_FAILURE detail=broken-commit-exact-conflict: expected conflict HTTP 400/409, got 200: {'writtenEvents': [{'payload': 'eyJmb3JlY2FzdElkIjoiZzA4Mi1hNTE3YWNhZGU1YzItYnJva2VuLWNvbW1pdCIsImxvY2F0aW9uIjoiVG9reW8iLCJ0ZW1wZXJhdHVyZUMiOjIxLCJzdW1tYXJ5IjoiU1dSLUcwODIiLCJjcmVhdGVkQXQiOiIyMDI2LTA4LTE3VDIxOjA2OjE3LjIwNloifQ==', 'sortableUniqueIdValue': '063922597577217464900082002382', 'id': '01a0118b-ea01-7f72-9ede-b3f36a0fb2f4', 'eventMetadata': {'causationId': '01a0118b-ea01-7f72-9ede-b3f36a0fb2f4', 'correlationId': 'SerializedCommit', 'executedUser': 'SerializedSekibanExecutor'}, 'tags': ['weather:g082-a517acade5c2'], 'eventPayloadName': 'WeatherForecastCreated'}], 'tagWriteResults': [{'tag': 'weather:g082-a517acade5c2', 'version': 1, 'writtenAt': '2026-08-17T21:06:17.2199331+00:00'}], 'duration': '00:00:00.0103440'}

## Findings boundary

The normative findings ledger is docs/compatibility/serialized-dcb-v1-findings.md. The run proves the observable scenarios; it does not convert provider partial-write or internal allocator-seed limits into zero findings.
