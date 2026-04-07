// Domain types matching Go's sekiban library
// Helper functions
export function tagString(group, id) {
    return `${group}:${id}`;
}
export function isEmptyJSON(raw) {
    if (raw === null || raw === undefined || raw === "" || raw === "{}") {
        return true;
    }
    return false;
}
export function newCommandOutput(eventType, payload, tags, consistencyTags, versions) {
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
    constructor(message = "already exists") {
        super(message);
        this.name = "AlreadyExistsError";
    }
}
export class NotFoundError extends Error {
    constructor(message = "not found") {
        super(message);
        this.name = "NotFoundError";
    }
}
export class ValidationError extends Error {
    constructor(message = "validation error") {
        super(message);
        this.name = "ValidationError";
    }
}
