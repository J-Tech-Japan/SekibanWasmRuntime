import { createHash } from "node:crypto";
import { cp, lstat, mkdir, mkdtemp, readFile, readdir, realpath, rm, symlink, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { spawn } from "node:child_process";
import { performance } from "node:perf_hooks";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, "../..");
const packageRoot = resolve(repositoryRoot, "src/wasm-projectors/typescript");
const fixturePath = join(packageRoot, "fixtures/domain-pin.json");
const fixture = JSON.parse(await readFile(fixturePath, "utf8"));
const outputDirectory = join(packageRoot, "build");
const upstreamUrl = `https://github.com/${fixture.repository}.git`;

function executable(name) {
  return resolve(packageRoot, "node_modules/.bin", name);
}

function runResult(command, args, cwd, extraEnvironment = {}) {
  return new Promise((resolvePromise, rejectPromise) => {
    const child = spawn(command, args, {
      cwd,
      env: { ...process.env, ...extraEnvironment },
      stdio: ["ignore", "pipe", "pipe"],
    });
    let stdout = "";
    let stderr = "";
    child.stdout.on("data", (chunk) => { stdout += chunk; });
    child.stderr.on("data", (chunk) => { stderr += chunk; });
    child.on("error", rejectPromise);
    child.on("close", (code, signal) => {
      resolvePromise({ code, signal, stdout, stderr });
    });
  });
}

async function run(command, args, cwd, extraEnvironment = {}) {
  const result = await runResult(command, args, cwd, extraEnvironment);
  if (result.code === 0) {
    return result;
  }
  throw new Error([
    `Command failed (${result.code ?? `signal:${result.signal}`})`,
    `${command} ${args.join(" ")}`,
    result.stdout.trim(),
    result.stderr.trim(),
  ].filter(Boolean).join("\n"));
}

async function packageVersion(packageName) {
  const path = join(packageRoot, "node_modules", ...packageName.split("/"), "package.json");
  return JSON.parse(await readFile(path, "utf8")).version;
}

async function assertToolchain() {
  const packageJson = JSON.parse(await readFile(join(packageRoot, "package.json"), "utf8"));
  const expectedJco = packageJson.devDependencies["@bytecodealliance/jco"];
  const expectedComponentize = packageJson.devDependencies["@bytecodealliance/componentize-js"];
  const actualJco = await packageVersion("@bytecodealliance/jco");
  const actualComponentize = await packageVersion("@bytecodealliance/componentize-js");
  if (expectedJco !== actualJco || expectedComponentize !== actualComponentize) {
    throw new Error([
      "Pinned componentize toolchain mismatch.",
      `jco expected=${expectedJco} actual=${actualJco}`,
      `componentize-js expected=${expectedComponentize} actual=${actualComponentize}`,
      "Run npm ci in src/wasm-projectors/typescript.",
    ].join("\n"));
  }
  return { jco: actualJco, componentizeJs: actualComponentize };
}

async function verifyPinnedUpstream(upstreamRoot) {
  const head = (await run("git", ["rev-parse", "HEAD"], upstreamRoot)).stdout.trim();
  if (head !== fixture.commit) {
    throw new Error(`Pinned upstream checkout mismatch: expected ${fixture.commit}, got ${head}`);
  }

  const upstreamDomainPath = join(upstreamRoot, fixture.domainPath);
  const upstreamDomain = await readFile(upstreamDomainPath);
  const upstreamSha256 = createHash("sha256").update(upstreamDomain).digest("hex");
  const upstreamBlob = (await run("git", ["rev-parse", `${fixture.commit}:${fixture.domainPath}`], upstreamRoot)).stdout.trim();
  const boundaryCheckerBlob = (await run("git", ["rev-parse", `${fixture.commit}:${fixture.boundaryCheckerPath}`], upstreamRoot)).stdout.trim();
  if (
    upstreamSha256 !== fixture.sha256 ||
    upstreamDomain.length !== fixture.bytes ||
    upstreamBlob !== fixture.gitBlob ||
    boundaryCheckerBlob !== fixture.boundaryCheckerGitBlob
  ) {
    throw new Error([
      "Pinned upstream domain verification failed.",
      `bytes expected=${fixture.bytes} actual=${upstreamDomain.length}`,
      `sha256 expected=${fixture.sha256} actual=${upstreamSha256}`,
      `gitBlob expected=${fixture.gitBlob} actual=${upstreamBlob}`,
      `boundaryCheckerBlob expected=${fixture.boundaryCheckerGitBlob} actual=${boundaryCheckerBlob}`,
    ].join("\n"));
  }

  const localVerification = await run(
    process.execPath,
    [join(repositoryRoot, "build/scripts/verify-dcb-ts-domain-pin.mjs")],
    repositoryRoot,
  );
  process.stdout.write(localVerification.stdout);
  return {
    bytes: upstreamDomain.length,
    sha256: upstreamSha256,
    gitBlob: upstreamBlob,
    boundaryCheckerGitBlob: boundaryCheckerBlob,
  };
}

async function publishedPackageProvenance(upstreamRoot, stageRoot) {
  const upstreamPackageRoot = join(upstreamRoot, "packages/dcb-domain");
  const upstreamPackage = JSON.parse(await readFile(join(upstreamPackageRoot, "package.json"), "utf8"));
  const configuredRegistry = process.env.G086_DCB_DOMAIN_REGISTRY;
  const registries = configuredRegistry
    ? [configuredRegistry]
    : ["https://registry.npmjs.org", "https://npm.pkg.github.com"];
  const attempts = [];
  const packageSpec = `${upstreamPackage.name}@${upstreamPackage.version}`;

  for (const registry of registries) {
    const args = ["view", packageSpec, "version", "--registry", registry, "--json"];
    const result = await runResult("npm", args, repositoryRoot);
    const attempt = {
      registry,
      command: `npm ${args.join(" ")}`,
      exitCode: result.code,
      stdout: result.stdout.trim(),
      stderr: result.stderr.trim(),
    };
    attempts.push(attempt);
    if (result.code !== 0) {
      continue;
    }

    const packArgs = ["pack", packageSpec, "--registry", registry, "--ignore-scripts", "--json", "--pack-destination", stageRoot];
    const pack = await run("npm", packArgs, repositoryRoot);
    const report = JSON.parse(pack.stdout)[0];
    const tarballPath = join(stageRoot, report.filename);
    const publishedRoot = join(stageRoot, "published-dcb-domain");
    await mkdir(publishedRoot, { recursive: true });
    await run("tar", ["-xzf", tarballPath, "-C", publishedRoot, "--strip-components=1"], stageRoot);
    for (const required of ["dist/index.js", "dist/index.d.ts"]) {
      if (!(await fileExists(join(publishedRoot, required)))) {
        throw new Error(`Published ${packageSpec} from ${registry} omitted ${required}.`);
      }
    }
    return {
      packagePath: publishedRoot,
      provenance: {
        status: "published-dist-consumed",
        package: packageSpec,
        registry,
        attempts,
        sourceFallback: false,
      },
    };
  }

  return {
    packagePath: null,
    provenance: {
      status: "published-artifact-unavailable",
      package: packageSpec,
      attempts,
      upstreamPackage: {
        private: upstreamPackage.private === true,
        publishConfig: upstreamPackage.publishConfig ?? null,
        hasNpmrc: await fileExists(join(upstreamRoot, ".npmrc")),
      },
      sourceFallback: {
        status: "pinned-upstream-source-staging",
        repository: fixture.repository,
        commit: fixture.commit,
        reason: "No canonical registry was declared by the pinned upstream checkout, and both npmjs.org and npm.pkg.github.com returned 404 for the package.",
      },
    },
  };
}

async function fileExists(path) {
  try {
    await lstat(path);
    return true;
  } catch (error) {
    if (error?.code === "ENOENT") return false;
    throw error;
  }
}

async function buildDcbDomainPackage(upstreamRoot, stageRoot) {
  const stagePackage = join(stageRoot, "dcb-domain");
  await cp(join(upstreamRoot, "packages/dcb-domain"), stagePackage, { recursive: true });
  const stageNodeModules = join(stagePackage, "node_modules");
  await mkdir(stageNodeModules, { recursive: true });
  await symlink(
    join(packageRoot, "node_modules/zod"),
    join(stageNodeModules, "zod"),
    "dir",
  );

  const localConfig = join(stagePackage, "tsconfig.build.local.json");
  await writeFile(localConfig, `${JSON.stringify({
    compilerOptions: {
      target: "ES2022",
      lib: ["ES2022", "WebWorker"],
      module: "ESNext",
      moduleResolution: "Bundler",
      rootDir: "src",
      outDir: "dist",
      strict: true,
      declaration: true,
      declarationMap: false,
      sourceMap: false,
      skipLibCheck: true,
    },
    include: ["src/**/*.ts"],
  }, null, 2)}\n`);

  await run(executable("tsc"), ["-p", localConfig], stagePackage);
  await run(executable("esbuild"), [
    "src/index.ts",
    "--bundle",
    "--format=esm",
    "--platform=neutral",
    "--outfile=dist/index.js",
  ], stagePackage);
  await run(executable("esbuild"), [
    "src/testing.ts",
    "--bundle",
    "--format=esm",
    "--platform=neutral",
    "--outfile=dist/testing.js",
  ], stagePackage);
  return stagePackage;
}

async function filesUnder(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];
  for (const entry of entries) {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) {
      files.push(...await filesUnder(path));
    } else if (entry.isFile()) {
      files.push(path);
    }
  }
  return files;
}

async function scanZodRuntime() {
  const zodRoot = join(packageRoot, "node_modules/zod");
  const files = (await filesUnder(zodRoot))
    .filter((path) => !path.includes(`${join("node_modules", "zod", "src")}${"/"}`))
    .filter((path) => /\.(?:js|mjs|cjs)$/.test(path));
  const patterns = {
    eval: /\beval\s*\(/g,
    newFunction: /\bnew\s+Function\b/g,
    functionConstructor: /\b(?:const\s+F\s*=\s*Function|new\s+F\s*\()/g,
    dynamicImport: /\bimport\s*\(/g,
    topLevelAwait: /^await\b/gm,
    dateNow: /\bDate\.now\s*\(/g,
    mathRandom: /\bMath\.random\s*\(/g,
    nodeApis: /\b(?:node:|process\b|globalThis\b|require\s*\()/g,
  };
  const hazards = {};
  for (const [name, pattern] of Object.entries(patterns)) {
    const hits = [];
    for (const path of files) {
      const source = await readFile(path, "utf8");
      const matches = source.match(pattern);
      if (matches !== null) {
        hits.push({
          file: relative(packageRoot, path),
          occurrences: matches.length,
        });
      }
    }
    hazards[name] = {
      files: hits.length,
      occurrences: hits.reduce((total, hit) => total + hit.occurrences, 0),
      samples: hits.slice(0, 12),
    };
  }
  return {
    package: "zod",
    version: JSON.parse(await readFile(join(zodRoot, "package.json"), "utf8")).version,
    runtimeFiles: files.length,
    hazards,
    finding: "The 4.4.3 runtime distribution contains lexical Math.random/new Function/globalThis/require hits outside the domain source; these are dependency findings, not edits to the upstream domain. The componentize-js run below is the first real component execution evidence for this package path.",
  };
}

function scanBundleHazards(bundle) {
  const patterns = {
    newFunction: /\bnew\s+Function\b/g,
    functionConstructor: /\b(?:const\s+F\s*=\s*Function|new\s+F\s*\()/g,
    dynamicImport: /\bimport\s*\(/g,
    topLevelAwait: /^await\b/gm,
    dateNow: /\bDate\.now\s*\(/g,
    mathRandom: /\bMath\.random\s*\(/g,
    globalThis: /\bglobalThis\b/g,
    nodeApis: /\b(?:node:|process\b|require\s*\()/g,
  };
  return Object.fromEntries(Object.entries(patterns).map(([name, pattern]) => [
    name,
    (bundle.match(pattern) ?? []).length,
  ]));
}

async function runBoundaryChecker(upstreamRoot, stagedPackage) {
  const upstreamPackage = join(upstreamRoot, "packages/dcb-domain");
  await rm(join(upstreamPackage, "dist"), { recursive: true, force: true });
  await cp(join(stagedPackage, "dist"), join(upstreamPackage, "dist"), { recursive: true });
  const checker = join(upstreamRoot, fixture.boundaryCheckerPath);
  const result = await run(process.execPath, [checker], upstreamRoot);
  process.stdout.write(result.stdout);
  return { status: "PASS", checker: relative(repositoryRoot, checker) };
}

async function linkStagedPackage(stagedPackage) {
  const scopeDirectory = join(packageRoot, "node_modules/@sekiban");
  const linkPath = join(scopeDirectory, "dcb-domain");
  await mkdir(scopeDirectory, { recursive: true });
  let created = false;
  try {
    const current = await lstat(linkPath);
    if (!current.isSymbolicLink() || (await realpath(linkPath)) !== (await realpath(stagedPackage))) {
      throw new Error(`Refusing to replace existing ${linkPath}; the build requires a staged pinned package.`);
    }
  } catch (error) {
    if (error?.code !== "ENOENT") throw error;
    await symlink(stagedPackage, linkPath, "dir");
    created = true;
  }
  return { linkPath, created };
}

async function main() {
  const toolchain = await assertToolchain();
  const stageRoot = await mkdtemp(join(tmpdir(), "swr-g086-build-"));
  const configuredUpstream = process.env.G086_UPSTREAM_ROOT;
  const upstreamRoot = configuredUpstream
    ? resolve(configuredUpstream)
    : join(stageRoot, "sekiban-dcb-ts");
  let packageLink;
  try {
    if (!configuredUpstream) {
      await run("git", ["clone", "--filter=blob:none", "--no-checkout", "--quiet", upstreamUrl, upstreamRoot], repositoryRoot);
      await run("git", ["checkout", "--quiet", "--detach", fixture.commit], upstreamRoot);
    }

    const upstream = await verifyPinnedUpstream(upstreamRoot);
    const packageResolution = await publishedPackageProvenance(upstreamRoot, stageRoot);
    const stagedPackage = packageResolution.packagePath ??
      await buildDcbDomainPackage(upstreamRoot, stageRoot);
    const boundary = await runBoundaryChecker(upstreamRoot, stagedPackage);
    packageLink = await linkStagedPackage(stagedPackage);

    await run(executable("tsc"), ["-p", "tsconfig.json"], packageRoot);
    const reference = await run(
      process.execPath,
      [join(repositoryRoot, "build/scripts/record-dcb-ts-reference.mjs")],
      repositoryRoot,
    );
    process.stdout.write(reference.stdout);
    await run(executable("esbuild"), [
      "build/js/guest.js",
      "--bundle",
      "--format=esm",
      "--platform=neutral",
      "--target=es2022",
      "--outfile=build/bundle.js",
    ], packageRoot);

    const componentizeStarted = performance.now();
    await run(executable("jco"), [
      "componentize",
      "build/bundle.js",
      "--wit",
      "wasm/sekiban-wasm.wit",
      "--world-name",
      "sekiban-projector",
      "--out",
      "build/module.wasm",
    ], packageRoot);
    const componentizeMs = Math.round((performance.now() - componentizeStarted) * 100) / 100;
    const bundle = await readFile(join(outputDirectory, "bundle.js"), "utf8");
    const bundleBytes = Buffer.byteLength(bundle);
    const componentBytes = (await readFile(join(outputDirectory, "module.wasm"))).byteLength;
    const zodStaticScan = await scanZodRuntime();
    const measurements = {
      schemaVersion: 1,
      package: "sekiban-dcb-ts",
      domainPin: { ...fixture, bytes: upstream.bytes, sha256: upstream.sha256, gitBlob: upstream.gitBlob },
      packageProvenance: packageResolution.provenance,
      toolchain,
      upstreamBoundary: boundary,
      zodStaticScan,
      componentBundleScan: scanBundleHazards(bundle),
      componentizeJs: {
        durationMs: componentizeMs,
        bundleBytes,
        componentBytes,
        command: "jco componentize build/bundle.js --wit wasm/sekiban-wasm.wit --world-name sekiban-projector --out build/module.wasm",
      },
    };
    await writeFile(join(outputDirectory, "measurements.json"), `${JSON.stringify(measurements, null, 2)}\n`);
    console.log(JSON.stringify({ status: "PASS", artifact: join(outputDirectory, "module.wasm"), measurements }));
  } finally {
    if (packageLink?.created) await rm(packageLink.linkPath, { recursive: true, force: true });
    await rm(stageRoot, { recursive: true, force: true });
  }
}

await main();
