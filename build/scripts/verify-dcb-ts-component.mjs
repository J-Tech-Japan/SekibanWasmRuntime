import { readFile } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { spawn } from "node:child_process";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, "../..");
const packageRoot = join(repositoryRoot, "src/wasm-projectors/typescript");
const artifactPath = join(packageRoot, "build/module.wasm");
const expectedExports = [
  "apply-event",
  "create-instance",
  "deserialize-event",
  "execute-list-query",
  "execute-query",
  "get-event-types",
  "restore-state",
  "serialize-event",
  "serialize-state",
];

function run(command, args, cwd) {
  return new Promise((resolvePromise, rejectPromise) => {
    const child = spawn(command, args, {
      cwd,
      env: process.env,
      stdio: ["ignore", "pipe", "pipe"],
    });
    let stdout = "";
    let stderr = "";
    child.stdout.on("data", (chunk) => { stdout += chunk; });
    child.stderr.on("data", (chunk) => { stderr += chunk; });
    child.on("error", rejectPromise);
    child.on("close", (code) => {
      if (code === 0) {
        resolvePromise(stdout);
        return;
      }
      rejectPromise(new Error([
        `${command} ${args.join(" ")} failed with exit code ${code}`,
        stdout.trim(),
        stderr.trim(),
      ].filter(Boolean).join("\n")));
    });
  });
}

const wasmTools = process.env.WASM_TOOLS ?? "wasm-tools";
await run(wasmTools, ["validate", artifactPath], repositoryRoot);
const wit = await run(wasmTools, ["component", "wit", artifactPath], repositoryRoot);
const actualExports = [...wit.matchAll(/^\s*export\s+([a-z][a-z0-9-]*):/gmi)]
  .map((match) => match[1])
  .sort();
const sortedExpected = [...expectedExports].sort();

if (JSON.stringify(actualExports) !== JSON.stringify(sortedExpected)) {
  throw new Error([
    "Guest export surface mismatch.",
    `expected=${JSON.stringify(sortedExpected)}`,
    `actual=${JSON.stringify(actualExports)}`,
    "The component must expose the exact WIT set; command exports are not permitted.",
  ].join("\n"));
}

if (/\bcommand\b/i.test(actualExports.join("\n"))) {
  throw new Error("Guest export surface contains a command entry.");
}

const pin = await run(
  process.execPath,
  [join(repositoryRoot, "build/scripts/verify-dcb-ts-domain-pin.mjs")],
  repositoryRoot,
);
process.stdout.write(pin);
const measurements = JSON.parse(await readFile(join(packageRoot, "build/measurements.json"), "utf8"));
if (
  measurements.componentizeJs?.durationMs === undefined ||
  measurements.componentizeJs?.bundleBytes === undefined ||
  measurements.componentizeJs?.componentBytes === undefined
) {
  throw new Error("Componentize-js measurements are missing from build/measurements.json.");
}
if (
  measurements.packageProvenance?.status === undefined ||
  measurements.zodStaticScan?.runtimeFiles === undefined ||
  measurements.componentBundleScan === undefined
) {
  throw new Error("Package provenance and dependency/static-scan evidence are missing from build/measurements.json.");
}
if (
  measurements.packageProvenance.status === "published-artifact-unavailable" &&
  measurements.packageProvenance.sourceFallback?.status !== "pinned-upstream-source-staging"
) {
  throw new Error("An unavailable published package must declare the pinned-source fallback explicitly.");
}

console.log(JSON.stringify({
  status: "PASS",
  artifact: artifactPath,
  exactExports: actualExports,
  packageProvenance: measurements.packageProvenance,
  zodStaticScan: measurements.zodStaticScan,
  componentBundleScan: measurements.componentBundleScan,
  componentizeJs: measurements.componentizeJs,
}));
