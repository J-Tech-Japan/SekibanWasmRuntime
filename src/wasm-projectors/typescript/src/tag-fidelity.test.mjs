import assert from "node:assert/strict";
import test from "node:test";
import { tagProbeProjector } from "../build/js/tag-probe-fixture.js";

const distinctiveTags = ["probe:alpha", "probe:beta", "probe:gamma"];

function applyProbeEvent(eventTags) {
  return tagProbeProjector.apply(tagProbeProjector.initialState, {
    eventType: "ProbePing",
    payload: {},
    eventTags,
    provenance: "g32",
  });
}

test("@sekiban/dcb-domain@0.2.0 preserves distinctive saved tags in projector state", () => {
  const next = applyProbeEvent(distinctiveTags);
  assert.deepEqual(next.observedTagIds, distinctiveTags);
});

test("@sekiban/dcb-domain@0.2.0 rejects eventTags: [] with RUNTIME_EVENT_TAGS_EMPTY", () => {
  assert.throws(
    () => applyProbeEvent([]),
    (error) => {
      assert.equal(error.code, "RUNTIME_EVENT_TAGS_EMPTY");
      assert.match(String(error), /empty eventTags list/);
      return true;
    },
  );
});

test("@sekiban/dcb-domain@0.2.0 synthesizes probe:__runtime__ when eventTags is omitted", () => {
  const next = tagProbeProjector.apply(tagProbeProjector.initialState, {
    eventType: "ProbePing",
    payload: {},
    provenance: "g32",
  });
  assert.deepEqual(next.observedTagIds, ["probe:__runtime__"]);
  assert.notDeepEqual(next.observedTagIds, distinctiveTags);
});
