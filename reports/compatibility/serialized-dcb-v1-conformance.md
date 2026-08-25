# Serialized DCB V1 external black-box run

- Result: **PASS**
- Source repository root: <source checkout>
- External suite repository root: <fresh temporary Git checkout>
- External invocation proven: **true** (fresh Git repository, different root, suite cwd recorded in JSON; temporary root is removed after the run)
- HTTP-only suite: **true** (Python standard library; no local implementation import)
- Restart lifecycle command executed: **true**
- Deliberately broken commit tag comparison (HTTP proxy) failed as expected: **true**
- Target: http://127.0.0.1:54780
- Suite source: conformance/serialized-dcb-v1/suite.py

## Scenario markers

    before.log:BEFORE_RESTART=PASS tag=weather:g082-af5306a28c63 head=063923241350801164200440151351
    before.log:CONFORMANCE_RESULT=PASS phase=before-restart requests=32
    after.log:AFTER_RESTART=PASS tag=weather:g082-af5306a28c63 old=063923241350801164200440151351 new=063923241355365707001493538173
    after.log:CONFORMANCE_RESULT=PASS phase=after-restart requests=6
    negative.log:CONFORMANCE_RESULT=FAIL detail=broken-commit-exact-conflict: expected conflict HTTP 400/409, got 200: {'writtenEvents': [{'payload': 'eyJmb3JlY2FzdElkIjoiZzA4Mi1hZjUzMDZhMjhjNjMtYnJva2VuLWNvbW1pdCIsImxvY2F0aW9uIjoiVG9reW8iLCJ0ZW1wZXJhdHVyZUMiOjIxLCJzdW1tYXJ5IjoiU1dSLUcwODIiLCJjcmVhdGVkQXQiOiIyMDI2LTA4LTI1VDA3OjU1OjU4LjE1NloifQ==', 'sortableUniqueIdValue': '063923241358166395700949356458', 'id': '01a037eb-3b56-7ec7-9f27-927435b2cf6b', 'eventMetadata': {'causationId': '01a037eb-3b56-7ec7-9f27-927435b2cf6b', 'correlationId': 'SerializedCommit', 'executedUser': 'SerializedSekibanExecutor'}, 'tags': ['weather:g082-af5306a28c63'], 'eventPayloadName': 'WeatherForecastCreated'}], 'tagWriteResults': [{'tag': 'weather:g082-af5306a28c63', 'version': 1, 'writtenAt': '2026-08-25T07:55:58.1713682+00:00'}], 'duration': '00:00:00.0080661'}
    negative.log:BROKEN_TAG_NEGATIVE=EXPECTED_FAILURE detail=broken-commit-exact-conflict: expected conflict HTTP 400/409, got 200: {'writtenEvents': [{'payload': 'eyJmb3JlY2FzdElkIjoiZzA4Mi1hZjUzMDZhMjhjNjMtYnJva2VuLWNvbW1pdCIsImxvY2F0aW9uIjoiVG9reW8iLCJ0ZW1wZXJhdHVyZUMiOjIxLCJzdW1tYXJ5IjoiU1dSLUcwODIiLCJjcmVhdGVkQXQiOiIyMDI2LTA4LTI1VDA3OjU1OjU4LjE1NloifQ==', 'sortableUniqueIdValue': '063923241358166395700949356458', 'id': '01a037eb-3b56-7ec7-9f27-927435b2cf6b', 'eventMetadata': {'causationId': '01a037eb-3b56-7ec7-9f27-927435b2cf6b', 'correlationId': 'SerializedCommit', 'executedUser': 'SerializedSekibanExecutor'}, 'tags': ['weather:g082-af5306a28c63'], 'eventPayloadName': 'WeatherForecastCreated'}], 'tagWriteResults': [{'tag': 'weather:g082-af5306a28c63', 'version': 1, 'writtenAt': '2026-08-25T07:55:58.1713682+00:00'}], 'duration': '00:00:00.0080661'}

## Findings boundary

The normative findings ledger is docs/compatibility/serialized-dcb-v1-findings.md. The run proves the observable scenarios; it does not convert provider partial-write or internal allocator-seed limits into zero findings.
