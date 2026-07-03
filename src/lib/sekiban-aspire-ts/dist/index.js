// @sekiban/aspire — thin helper for Aspire TypeScript AppHosts.
//
// The Aspire TS AppHost API is a per-app generated module
// (`./.aspire/modules/aspire.mjs`), not an npm package, so this helper is
// written against minimal structural types that the generated fluent builder
// satisfies. It stays a single sample-driven function on top of the official
// APIs — deliberately not a port of the C# Sekiban.Dcb.WasmRuntime.Aspire
// options abstraction.
/** Default public runtime image. */
export const DEFAULT_RUNTIME_IMAGE = "ghcr.io/j-tech-japan/sekiban-wasm-runtime-host";
/** Default verified public tag. */
export const DEFAULT_RUNTIME_IMAGE_TAG = "1.0.0-preview.3";
/**
 * Resolve the runtime image tag the same way the repository samples do:
 * the SAMPLE_RUNTIME_IMAGE_TAG environment variable wins, otherwise the
 * verified default tag.
 */
export function resolveRuntimeImageTag(fallback = DEFAULT_RUNTIME_IMAGE_TAG) {
    const override = process.env.SAMPLE_RUNTIME_IMAGE_TAG;
    return override && override.length > 0 ? override : fallback;
}
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
export function addSekibanWasmRuntime(builder, name, config) {
    if (!config.wasmModulePath) {
        throw new Error("wasmModulePath must be set to the in-container path of the projector module, e.g. /app/modules/my-projector.wasm");
    }
    const image = config.image ?? DEFAULT_RUNTIME_IMAGE;
    const tag = config.tag ?? resolveRuntimeImageTag();
    const targetPort = config.targetPort ?? 8080;
    const manifestPath = config.manifestPath ?? "/app/config/sekiban-manifest.json";
    let runtime = builder
        .addContainer(name, { image, tag })
        .withBindMount(config.configDirectory, "/app/config", { isReadOnly: true })
        .withBindMount(config.modulesDirectory, "/app/modules", { isReadOnly: true })
        .withEnvironment("ASPNETCORE_URLS", `http://0.0.0.0:${targetPort}`)
        .withEnvironment("SEKIBAN_PROJECTION_MODE", config.projectionMode ?? "dual")
        .withEnvironment("SEKIBAN_MANIFEST_PATH", manifestPath)
        .withEnvironment("WASM_MODULE_PATH", config.wasmModulePath);
    for (const reference of config.references ?? []) {
        runtime = runtime.withReference(reference);
    }
    // Overrides win over the standard contract above.
    for (const [key, value] of Object.entries(config.env ?? {})) {
        runtime = runtime.withEnvironment(key, value);
    }
    runtime =
        config.hostPort && config.hostPort > 0
            ? runtime.withHttpEndpoint({ targetPort, port: config.hostPort, name: "http", isProxied: false })
            : runtime.withHttpEndpoint({ targetPort, name: "http" });
    return runtime.withExternalHttpEndpoints();
}
