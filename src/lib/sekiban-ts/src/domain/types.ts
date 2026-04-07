// Domain types matching Go's sekiban library

export interface TagStateResponse {
  stateJson: string;
  version: number;
  lastSortableUniqueId?: string | null;
}

export interface CommandContext {
  getTagState(tagGroup: string, id: string): Promise<TagStateResponse>;
  cache: Map<string, TagStateResponse>;
}

export interface EventOutput {
  eventType: string;
  payload: any;
  tags: string[];
  versions: Record<string, number>;
}

export interface CommandOutput {
  events: EventOutput[];
  tags: string[];
  consistencyTags: string[];
}

export interface CountResult {
  count: number;
}

export interface Command {
  commandType(): string;
  handle(ctx: CommandContext): Promise<CommandOutput>;
}

// Helper functions

export function tagString(group: string, id: string): string {
  return `${group}:${id}`;
}

export function isEmptyJSON(raw: string | null | undefined): boolean {
  if (raw === null || raw === undefined || raw === "" || raw === "{}") {
    return true;
  }
  return false;
}

export function newCommandOutput(
  eventType: string,
  payload: any,
  tags: string[],
  consistencyTags: string[],
  versions: Record<string, number>,
): CommandOutput {
  return {
    events: [
      {
        eventType,
        payload,
        tags,
        versions,
      },
    ],
    tags,
    consistencyTags,
  };
}

// Error classes

export class AlreadyExistsError extends Error {
  constructor(message: string = "already exists") {
    super(message);
    this.name = "AlreadyExistsError";
  }
}

export class NotFoundError extends Error {
  constructor(message: string = "not found") {
    super(message);
    this.name = "NotFoundError";
  }
}

export class ValidationError extends Error {
  constructor(message: string = "validation error") {
    super(message);
    this.name = "ValidationError";
  }
}
