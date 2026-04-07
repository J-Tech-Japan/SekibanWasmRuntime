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
export declare function tagString(group: string, id: string): string;
export declare function isEmptyJSON(raw: string | null | undefined): boolean;
export declare function newCommandOutput(eventType: string, payload: any, tags: string[], consistencyTags: string[], versions: Record<string, number>): CommandOutput;
export declare class AlreadyExistsError extends Error {
    constructor(message?: string);
}
export declare class NotFoundError extends Error {
    constructor(message?: string);
}
export declare class ValidationError extends Error {
    constructor(message?: string);
}
