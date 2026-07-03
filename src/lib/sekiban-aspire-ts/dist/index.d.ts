/** Fluent container resource surface used by the helper (subset of the generated API). */
export interface SekibanRuntimeContainer {
    withBindMount(source: string, target: string, options?: {
        isReadOnly?: boolean;
    }): SekibanRuntimeContainer;
    withEnvironment(name: string, value: string): SekibanRuntimeContainer;
    withReference(source: unknown, options?: unknown): SekibanRuntimeContainer;
    withHttpEndpoint(options?: {
        port?: number;
        targetPort?: number;
        name?: string;
        isProxied?: boolean;
    }): SekibanRuntimeContainer;
    withExternalHttpEndpoints(): SekibanRuntimeContainer;
}
/** Builder surface used by the helper (subset of the generated `createBuilder()` result). */
export interface SekibanAppHostBuilder {
    addContainer(name: string, image: string | {
        image?: string;
        tag?: string | null;
    }): SekibanRuntimeContainer;
}
export interface SekibanWasmRuntimeConfig {
    /** Container image. Defaults to the public GHCR image. */
    image?: string;
    /** Image tag. Defaults to resolveRuntimeImageTag() (SAMPLE_RUNTIME_IMAGE_TAG override, else 1.0.0-preview.3). */
    tag?: string;
    /** Host directory bind-mounted read-only at /app/config (runtime manifest). */
    configDirectory: string;
    /** Host directory bind-mounted read-only at /app/modules (wasm modules). */
    modulesDirectory: string;
    /** In-container path of the default projector module (WASM_MODULE_PATH), e.g. /app/modules/my.wasm. */
    wasmModulePath: string;
    /** In-container manifest path (SEKIBAN_MANIFEST_PATH). Defaults to /app/config/sekiban-manifest.json. */
    manifestPath?: string;
    /** SEKIBAN_PROJECTION_MODE. Defaults to "dual". */
    projectionMode?: string;
    /**
     * Resources injected via withReference — typically the Postgres databases
     * named `SekibanDcb` (event store) and `DcbMaterializedViewPostgres`
     * (materialized views); those exact names become the connection-string keys
     * the runtime expects.
     */
    references?: unknown[];
    /** Extra or overriding environment variables, applied after the standard contract. */
    env?: Record<string, string>;
    /** Fixed host port (published unproxied so scripts reach the runtime deterministically). */
    hostPort?: number;
    /** Container port the runtime listens on. Defaults to 8080. */
    targetPort?: number;
}
/** Default public runtime image. */
export declare const DEFAULT_RUNTIME_IMAGE = "ghcr.io/j-tech-japan/sekiban-wasm-runtime-host";
/** Default verified public tag. */
export declare const DEFAULT_RUNTIME_IMAGE_TAG = "1.0.0-preview.3";
/**
 * Resolve the runtime image tag the same way the repository samples do:
 * the SAMPLE_RUNTIME_IMAGE_TAG environment variable wins, otherwise the
 * verified default tag.
 */
export declare function resolveRuntimeImageTag(fallback?: string): string;
/**
 * Register the public Sekiban WASM Runtime container on an Aspire TypeScript
 * AppHost builder: GHCR image, read-only /app/config and /app/modules bind
 * mounts, the runtime environment contract (ASPNETCORE_URLS,
 * SEKIBAN_PROJECTION_MODE, SEKIBAN_MANIFEST_PATH, WASM_MODULE_PATH),
 * connection references, env overrides, and an HTTP endpoint.
 *
 * Returns the fluent container resource so callers can chain further official
 * Aspire APIs. No WaitFor gate is added on the databases: the runtime connects
 * to Postgres lazily and retries.
 */
export declare function addSekibanWasmRuntime(builder: SekibanAppHostBuilder, name: string, config: SekibanWasmRuntimeConfig): SekibanRuntimeContainer;
