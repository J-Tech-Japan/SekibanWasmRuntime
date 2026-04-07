import { tagString, } from "../domain/types.js";
export class SekibanRuntimeClient {
    baseUrl;
    tagProjectorMap;
    constructor(baseUrl, tagProjectorMap) {
        this.baseUrl = baseUrl.replace(/\/+$/, "");
        this.tagProjectorMap = tagProjectorMap;
    }
    async postJSON(path, body) {
        const resp = await fetch(`${this.baseUrl}${path}`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body),
        });
        if (!resp.ok) {
            const errText = await resp.text();
            throw new Error(`${path} failed: ${resp.status} ${resp.statusText} - ${errText}`);
        }
        return await resp.json();
    }
    async getTagState(tagGroup, id) {
        const projectorName = this.tagProjectorMap[tagGroup];
        if (!projectorName) {
            throw new Error(`no projector mapped for tag group: ${tagGroup}`);
        }
        const tagStateId = `${tagGroup}:${id}:${projectorName}`;
        const resp = await this.postJSON("/api/sekiban/serialized/tag-state", {
            tagStateId,
        });
        let stateJson = "{}";
        if (resp.payload && resp.payload !== "") {
            try {
                const decoded = Buffer.from(resp.payload, "base64").toString("utf-8");
                if (decoded.length > 0)
                    stateJson = decoded;
            }
            catch {
                // ignore decode errors
            }
        }
        const lastSortable = resp.lastSortableUniqueId || resp.lastSortedUniqueId || null;
        return {
            stateJson,
            version: resp.version ?? 0,
            lastSortableUniqueId: lastSortable,
        };
    }
    async executeQuery(queryType, paramsJson, serviceId) {
        const body = {
            queryType,
            queryParamsJson: paramsJson,
        };
        if (serviceId)
            body.serviceId = serviceId;
        const resp = await this.postJSON("/api/sekiban/serialized/query", body);
        return resp.resultJson ?? "null";
    }
    async executeListQuery(queryType, paramsJson, serviceId) {
        const body = {
            queryType,
            queryParamsJson: paramsJson,
        };
        if (serviceId)
            body.serviceId = serviceId;
        const resp = await this.postJSON("/api/sekiban/serialized/list-query", body);
        return resp.itemsJson ?? "[]";
    }
    async finalizeCommand(cmd) {
        const cache = new Map();
        const ctx = {
            cache,
            getTagState: async (tagGroup, id) => {
                const key = tagString(tagGroup, id);
                const cached = cache.get(key);
                if (cached) {
                    return cached;
                }
                const state = await this.getTagState(tagGroup, id);
                cache.set(key, state);
                return state;
            },
        };
        const output = await cmd.handle(ctx);
        // Build event candidates
        const eventCandidates = output.events.map((ev) => {
            const payloadJson = JSON.stringify(ev.payload);
            const payloadBase64 = Buffer.from(payloadJson).toString("base64");
            return {
                eventPayloadName: ev.eventType,
                payload: payloadBase64,
                tags: ev.tags,
            };
        });
        // Build consistency tags from cached state
        const seen = new Set();
        const consistencyTags = [];
        for (const cTag of output.consistencyTags) {
            if (seen.has(cTag))
                continue;
            seen.add(cTag);
            const cached = cache.get(cTag);
            const lastId = cached?.lastSortableUniqueId ?? "";
            consistencyTags.push({
                tag: cTag,
                lastSortableUniqueId: lastId,
            });
        }
        const commitBody = {
            eventCandidates,
            consistencyTags,
        };
        return await this.postJSON("/api/sekiban/serialized/commit", commitBody);
    }
}
