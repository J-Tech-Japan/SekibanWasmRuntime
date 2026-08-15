# Serialized DCB V1 external black-box run

- Result: **PASS**
- Source repository root: <source checkout>
- External suite repository root: <fresh temporary Git checkout>
- External invocation proven: **true** (fresh Git repository, different root, suite cwd recorded in JSON; temporary root was removed after the run)
- HTTP-only suite: **true** (Python standard library; no local implementation import)
- Restart lifecycle command executed: **true**
- Deliberately broken tag implementation (HTTP proxy) failed as expected: **true**
- Target: http://127.0.0.1:51080
- Suite source: conformance/serialized-dcb-v1/suite.py

## Scenario markers

    before.log:BEFORE_RESTART=PASS tag=weather:g082-68f8d7704635 head=063922369246145951601653300237
    before.log:CONFORMANCE_RESULT=PASS phase=before-restart requests=32
    after.log:AFTER_RESTART=PASS tag=weather:g082-68f8d7704635 old=063922369246145951601653300237 new=063922369252683973801787943563
    after.log:CONFORMANCE_RESULT=PASS phase=after-restart requests=6
    negative.log:CONFORMANCE_RESULT=FAIL detail=deliberately broken tag response was accepted
    negative.log:BROKEN_TAG_NEGATIVE=EXPECTED_FAILURE detail=deliberately broken tag response was accepted

## Findings boundary

The normative findings ledger is docs/compatibility/serialized-dcb-v1-findings.md. The run proves the observable scenarios; it does not convert provider partial-write or internal allocator-seed limits into zero findings.
