import { type TagStateResponse, type Command } from "../domain/types.js";
export declare class SekibanRuntimeClient {
    private baseUrl;
    private tagProjectorMap;
    constructor(baseUrl: string, tagProjectorMap: Record<string, string>);
    private postJSON;
    getTagState(tagGroup: string, id: string): Promise<TagStateResponse>;
    executeQuery(queryType: string, paramsJson: string, serviceId?: string | null): Promise<string>;
    executeListQuery(queryType: string, paramsJson: string, serviceId?: string | null): Promise<string>;
    finalizeCommand(cmd: Command): Promise<any>;
}
