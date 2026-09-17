import assert from "node:assert/strict";
import { describe, it } from "node:test";
import type { ExecuteResult, SekibanExecutor } from "@sekiban/dcb-client";
import type { CommandDefinition } from "@sekiban/dcb-domain";
import { executeOrThrow, writeErrorFromResult } from "./executorAdapter.js";

function result(partial: Record<string, unknown>): ExecuteResult {
  return { attempts: 1, ...partial } as ExecuteResult;
}

describe("writeErrorFromResult kind/code mapping", () => {
  const cases: Array<{ label: string; input: ExecuteResult; status: number; error: string }> = [
    { label: "rejected/not_found", input: result({ kind: "rejected", code: "not_found", error: "missing" }), status: 404, error: "NotFound" },
    { label: "rejected/validation_error", input: result({ kind: "rejected", code: "validation_error", error: "bad field" }), status: 400, error: "Validation" },
    { label: "rejected/consistency_conflict", input: result({ kind: "rejected", code: "consistency_conflict", error: "stale" }), status: 409, error: "AlreadyExists" },
    { label: "rejected/forbidden", input: result({ kind: "rejected", code: "forbidden", error: "denied" }), status: 500, error: "InternalError" },
    { label: "rejected/internal_error", input: result({ kind: "rejected", code: "internal_error", error: "boom" }), status: 500, error: "InternalError" },
    { label: "conflict/consistency_conflict", input: result({ kind: "conflict", code: "consistency_conflict", error: "exhausted" }), status: 409, error: "AlreadyExists" },
    { label: "invalid", input: result({ kind: "invalid", code: "invalid_input", error: "parse" }), status: 400, error: "Validation" },
    { label: "transport", input: result({ kind: "transport", code: "invalid_read_snapshot", error: "malformed" }), status: 500, error: "InternalError" },
    { label: "timeout", input: result({ kind: "timeout", code: "timeout", error: "slow" }), status: 500, error: "InternalError" },
    { label: "unavailable", input: result({ kind: "unavailable", code: "unavailable", error: "down" }), status: 500, error: "InternalError" },
    { label: "partial", input: result({ kind: "partial", code: "partial", error: "incomplete" }), status: 500, error: "InternalError" },
    { label: "noop", input: result({ kind: "noop", code: "noop", error: "already applied" }), status: 500, error: "InternalError" },
  ];

  for (const { label, input, status, error } of cases) {
    it(`maps ${label} to HTTP ${status}/${error}`, () => {
      const mapped = writeErrorFromResult(input);
      assert.equal(mapped.status, status);
      assert.equal(mapped.body.error, error);
    });
  }
});

describe("executeOrThrow success kinds", () => {
  it("returns committed results without throwing", async () => {
    const executor = {
      execute: async () => result({ kind: "committed", events: [] }),
    } as unknown as SekibanExecutor;
    const out = await executeOrThrow(executor, {} as CommandDefinition, {} as never);
    assert.equal(out.kind, "committed");
  });

  it("returns noop results without throwing", async () => {
    const executor = {
      execute: async () => result({ kind: "noop" }),
    } as unknown as SekibanExecutor;
    const out = await executeOrThrow(executor, {} as CommandDefinition, {} as never);
    assert.equal(out.kind, "noop");
  });
});
