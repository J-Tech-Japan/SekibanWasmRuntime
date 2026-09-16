import type { ExecuteResult, SekibanExecutor } from "@sekiban/dcb-client";
import type { CommandDefinition, CommandInput } from "@sekiban/dcb-domain";

export class HttpCommandError extends Error {
  readonly status: number;
  readonly code: string;

  constructor(status: number, code: string, message: string) {
    super(message);
    this.name = "HttpCommandError";
    this.status = status;
    this.code = code;
  }
}

function statusForResult(result: ExecuteResult): number {
  switch (result.kind) {
    case "rejected":
      if (result.code === "not_found") return 404;
      if (result.code === "validation_error") return 400;
      if (result.code === "consistency_conflict") return 409;
      return 500;
    case "conflict":
      return 409;
    case "invalid":
      return 400;
    case "timeout":
    case "unavailable":
    case "transport":
    case "partial":
      return 500;
    default:
      return 500;
  }
}

export function httpErrorFromResult(result: ExecuteResult): HttpCommandError {
  const status = statusForResult(result);
  const code = result.code ?? result.kind;
  const message = "error" in result && typeof result.error === "string"
    ? result.error
    : result.kind;
  return new HttpCommandError(status, code, message);
}

export async function executeOrThrow<C extends CommandDefinition>(
  executor: SekibanExecutor,
  command: C,
  input: CommandInput<C>,
) {
  const result = await executor.execute(command, input);
  if (result.kind === "committed") {
    return result;
  }
  throw httpErrorFromResult(result);
}
