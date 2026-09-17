import type { ExecuteCommitted, ExecuteNoop, ExecuteResult, SekibanExecutor } from "@sekiban/dcb-client";
import type { CommandDefinition, CommandInput } from "@sekiban/dcb-domain";

export class HttpCommandError extends Error {
  readonly status: number;
  readonly error: string;

  constructor(status: number, error: string, message: string) {
    super(message);
    this.name = "HttpCommandError";
    this.status = status;
    this.error = error;
  }
}

export function writeErrorFromResult(result: ExecuteResult): { status: number; body: { error: string; message: string } } {
  if (result.kind === "rejected") {
    if (result.code === "not_found") {
      return { status: 404, body: { error: "NotFound", message: result.error ?? "not found" } };
    }
    if (result.code === "validation_error") {
      return { status: 400, body: { error: "Validation", message: result.error ?? "validation error" } };
    }
    if (result.code === "consistency_conflict") {
      return { status: 409, body: { error: "AlreadyExists", message: result.error ?? "conflict" } };
    }
    return { status: 500, body: { error: "InternalError", message: result.error ?? result.code ?? "rejected" } };
  }
  if (result.kind === "conflict") {
    return { status: 409, body: { error: "AlreadyExists", message: result.error ?? "consistency conflict" } };
  }
  if (result.kind === "invalid") {
    return { status: 400, body: { error: "Validation", message: result.error ?? "invalid" } };
  }
  return { status: 500, body: { error: "InternalError", message: result.error ?? result.kind } };
}

export function writeErrorFromCommand(err: unknown): { status: number; body: { error: string; message: string } } {
  if (err instanceof HttpCommandError) {
    return { status: err.status, body: { error: err.error, message: err.message } };
  }
  const message = err instanceof Error ? err.message : String(err);
  return { status: 500, body: { error: "InternalError", message } };
}

export type ExecuteSuccess = ExecuteCommitted | ExecuteNoop;

export function responseFromExecuteSuccess(result: ExecuteSuccess): unknown {
  if (result.kind === "committed") {
    return result.response;
  }
  return { noop: true, reason: result.reason ?? null };
}

export async function executeOrThrow<C extends CommandDefinition>(
  executor: SekibanExecutor,
  command: C,
  input: CommandInput<C>,
): Promise<ExecuteSuccess> {
  const result = await executor.execute(command, input);
  if (result.kind === "committed" || result.kind === "noop") {
    return result;
  }
  const mapped = writeErrorFromResult(result);
  throw new HttpCommandError(mapped.status, mapped.body.error, mapped.body.message);
}

export async function readStateOrThrow(
  executor: SekibanExecutor,
  projector: Parameters<SekibanExecutor["readState"]>[0],
  tag: Parameters<SekibanExecutor["readState"]>[1],
) {
  const snapshot = await executor.readState(projector, tag);
  if (!snapshot.exists) {
    throw new HttpCommandError(404, "NotFound", `state not found for ${tag.id}`);
  }
  return snapshot;
}
