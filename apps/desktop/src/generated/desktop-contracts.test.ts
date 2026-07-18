import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import * as generatedContracts from "./desktop-contracts";

interface DesktopContractSource {
  protocolVersion: string;
  schemaVersion: number;
  capabilities: Array<{ name: string; required: boolean }>;
  errors: Array<{ code: string; rpcCode: number }>;
  frameErrors: string[];
  limits: Record<string, number>;
  methods: Array<{
    name: string;
    params: string;
    result: string;
    requiresWorkspace: boolean;
    mutation: boolean;
    timeout: "initialize" | "query" | "mutation" | "shutdown";
  }>;
  notifications: Array<{ name: string; params: string }>;
}

type JsonObject = Record<string, unknown>;

const repoRoot = path.resolve(import.meta.dirname, "../../../..");
const contractPath = path.join(repoRoot, "protocol", "desktop-v1", "contract.json");
const generatorPath = path.join(repoRoot, "apps", "desktop", "scripts", "generate-contracts.mjs");
const tempDirectories: string[] = [];

afterEach(async () => {
  await Promise.all(tempDirectories.splice(0).map((directory) =>
    rm(directory, { force: true, recursive: true })));
});

async function readContract(): Promise<DesktopContractSource> {
  return JSON.parse(await readFile(contractPath, "utf8")) as DesktopContractSource;
}

function validTestContract(): JsonObject {
  return {
    protocolVersion: "test-v1",
    schemaVersion: 1,
    capabilities: [],
    errors: [],
    frameErrors: [],
    limits: { maxBodyBytes: 1024 },
    methods: [{
      constant: "PingMethod",
      name: "app.ping",
      params: "PingParams",
      result: "PingResult",
      requiresWorkspace: false,
      mutation: false,
      timeout: "query",
    }],
    notifications: [],
    types: [
      {
        name: "PingParams",
        properties: [{ jsonName: "value", type: "string", maxUtf8Bytes: 64 }],
      },
      {
        name: "PingResult",
        properties: [{ jsonName: "accepted", type: "boolean" }],
      },
    ],
  };
}

function richTestContract(): JsonObject {
  const source = validTestContract();
  source.types = [
    {
      name: "Details",
      properties: [{ jsonName: "enabled", type: "boolean" }],
    },
    {
      name: "PingParams",
      properties: [
        { jsonName: "schemaVersion", type: "int32", min: 1, max: 1 },
        { jsonName: "value", type: "string", maxUtf8Bytes: 64, enum: ["one", "two"] },
        { jsonName: "count", type: "int64", min: 0, max: 9_007_199_254_740_991 },
        { jsonName: "occurredAtUtc", type: "datetime" },
        { jsonName: "details", type: "Details", nullable: true },
        {
          jsonName: "labels",
          type: "array",
          items: "string",
          maxItems: 4,
          maxUtf8Bytes: 32,
          optional: true,
        },
      ],
    },
    {
      name: "PingResult",
      properties: [{ jsonName: "accepted", type: "boolean" }],
    },
  ];
  return source;
}

function canonicalJson(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(",")}]`;
  if (value !== null && typeof value === "object") {
    return `{${Object.entries(value as JsonObject)
      .sort(([left], [right]) => left.localeCompare(right))
      .map(([key, entry]) => `${JSON.stringify(key)}:${canonicalJson(entry)}`)
      .join(",")}}`;
  }
  return JSON.stringify(value);
}

async function validateFixture(source: JsonObject) {
  const directory = await mkdtemp(path.join(os.tmpdir(), "caicli-contract-test-"));
  tempDirectories.push(directory);
  const fixturePath = path.join(directory, "contract.json");
  await writeFile(fixturePath, JSON.stringify(source), "utf8");
  return spawnSync(process.execPath, [
    generatorPath,
    "--check",
    "--validate-only",
    "--contract",
    fixturePath,
  ], {
    cwd: repoRoot,
    encoding: "utf8",
  });
}

describe("desktop-v1 contract source", () => {
  it("freezes the Week 73 method and notification allowlists", async () => {
    const contract = await readContract();

    expect(contract.protocolVersion).toBe("desktop-v1");
    expect(contract.schemaVersion).toBe(1);
    expect(contract.methods.map(({ name }) => name)).toEqual([
      "app.initialize",
      "app.cancel",
      "app.shutdown",
      "workspace.open",
      "thread.list",
      "thread.get",
      "thread.create",
      "thread.rename",
      "thread.archive",
      "thread.delete",
      "catalog.list",
      "context.search",
      "context.resolve",
      "composer.get",
      "composer.enqueue",
      "composer.clear",
      "turn.start",
      "turn.cancel",
      "approval.resolve",
      "turn.resume",
      "turn.restart",
      "changes.get",
      "report.list",
      "report.get",
      "artifact.list",
      "artifact.get",
    ]);
    expect(contract.methods.filter(({ requiresWorkspace }) => requiresWorkspace).map(({ name }) => name))
      .toEqual(contract.methods.slice(4).map(({ name }) => name));
    expect(contract.methods.filter(({ mutation }) => mutation).map(({ name }) => name)).toEqual([
      "thread.create",
      "thread.rename",
      "thread.archive",
      "thread.delete",
      "composer.enqueue",
      "composer.clear",
      "turn.start",
      "turn.cancel",
      "approval.resolve",
      "turn.resume",
      "turn.restart",
    ]);
    expect(contract.methods.filter(({ timeout }) => timeout === "mutation").map(({ name }) => name))
      .toEqual(contract.methods.filter(({ mutation }) => mutation).map(({ name }) => name));
    expect(generatedContracts.DESKTOP_METHOD_METADATA.ThreadCreateMethod).toEqual({
      requiresWorkspace: true,
      mutation: true,
      timeout: "mutation",
    });
    expect(contract.notifications.map(({ name }) => name)).toEqual(["thread.changed"]);
  });

  it("freezes capabilities, protocol errors, and numeric limits", async () => {
    const contract = await readContract();

    expect(contract.capabilities).toEqual([
      { name: "framed-json-rpc", required: true },
      { name: "workspace-session", required: true },
      { name: "application-outcome", required: true },
      { name: "composer.controlled-context", required: false },
      { name: "thread.changed", required: false },
      { name: "turn.write-path", required: false },
    ]);
    expect(contract.errors.map(({ code }) => code)).toEqual([
      "parse-error",
      "invalid-request",
      "method-not-found",
      "invalid-params",
      "internal-error",
      "initialize-required",
      "already-initialized",
      "protocol-version-unsupported",
      "contract-mismatch",
      "capability-invalid",
      "workspace-required",
      "workspace-changed",
      "shutdown-in-progress",
      "duplicate-request-id",
      "server-busy",
      "request-rate-exceeded",
      "request-canceled",
      "request-timeout",
      "response-too-large",
      "output-backpressure",
      "transport-closed",
    ]);
    expect(contract.frameErrors).toEqual([
      "frame-header-incomplete",
      "frame-header-too-large",
      "frame-header-invalid",
      "frame-content-length-missing",
      "frame-content-length-invalid",
      "frame-body-too-large",
      "frame-body-incomplete",
      "frame-body-empty",
      "frame-utf8-invalid",
    ]);
    expect(contract.limits).toEqual({
      maxHeaderBytes: 8_192,
      maxBodyBytes: 1_048_576,
      maxJsonDepth: 64,
      maxMethodNameBytes: 128,
      maxRequestId: 9_007_199_254_740_991,
      initializeTimeoutMs: 5_000,
      defaultQueryTimeoutMs: 15_000,
      mutationTimeoutMs: 30_000,
      shutdownDrainMs: 2_000,
      maxInFlight: 8,
      requestRateBurst: 64,
      requestRatePerSecond: 32,
      maxOutputQueueFrames: 64,
      maxOutputQueueBytes: 4_194_304,
      maxDiagnosticBytes: 4_096,
      maxRetainedStderrBytes: 16_384,
      applicationTargetBytes: 786_432,
      maxPromptBytes: 65_536,
      maxContextSelections: 32,
      maxCatalogSelections: 16,
      maxExecutionInputBytes: 262_144,
      maxSingleFileBytes: 10_485_760,
      maxTotalFileBytes: 33_554_432,
      maxFolderFiles: 500,
      maxFolderBytes: 67_108_864,
      maxContextSearchResults: 100,
      maxContextScannedEntries: 5_000,
      maxContextSearchQueryBytes: 256,
      maxRelativePathBytes: 4_096,
      maxQueueMutationIdBytes: 128,
      maxTimelineAppendItems: 32,
      maxTimelineAppendBytes: 262_144,
      maxAssistantPreviewBytes: 8_192,
      maxApprovalSummaryBytes: 4_096,
      approvalLifetimeMs: 1_800_000,
      cancelAcknowledgementMs: 5_000,
    });
  });

  it("rejects unbounded strings without writing generated files", async () => {
    const source = validTestContract();
    const types = source.types as Array<JsonObject>;
    const properties = types[0]?.properties as Array<JsonObject>;
    delete properties[0]?.maxUtf8Bytes;

    const result = await validateFixture(source);

    expect(result.status).toBe(1);
    expect(result.stderr).toContain("contract-string-unbounded");
  });

  it("rejects unsafe, ambiguous, or unbounded DSL constructs", async () => {
    const cases: Array<[string, (source: JsonObject) => void]> = [
      ["contract-root-member-unknown", (source) => { source.unreviewed = true; }],
      ["contract-identifier-unsafe", (source) => {
        const types = source.types as Array<JsonObject>;
        if (types[0]) types[0].name = "Bad;Type";
      }],
      ["contract-type-duplicate", (source) => {
        const types = source.types as Array<JsonObject>;
        if (types[0]) types.push(structuredClone(types[0]));
      }],
      ["contract-type-reference-missing", (source) => {
        const methods = source.methods as Array<JsonObject>;
        if (methods[0]) methods[0].result = "MissingResult";
      }],
      ["contract-array-unbounded", (source) => {
        const types = source.types as Array<JsonObject>;
        const properties = types[0]?.properties as Array<JsonObject>;
        if (properties[0]) properties[0] = { jsonName: "values", type: "array", items: "string" };
      }],
      ["contract-number-unbounded", (source) => {
        const types = source.types as Array<JsonObject>;
        const properties = types[0]?.properties as Array<JsonObject>;
        if (properties[0]) properties[0] = { jsonName: "count", type: "int32" };
      }],
      ["contract-enum-invalid", (source) => {
        const types = source.types as Array<JsonObject>;
        const properties = types[0]?.properties as Array<JsonObject>;
        if (properties[0]) properties[0] = {
          jsonName: "kind",
          type: "string",
          maxUtf8Bytes: 64,
          enum: ["same", "same"],
        };
      }],
      ["contract-object-cycle", (source) => {
        const types = source.types as Array<JsonObject>;
        const properties = types[0]?.properties as Array<JsonObject>;
        if (properties[0]) properties[0] = { jsonName: "self", type: "PingParams" };
      }],
      ["contract-method-metadata-invalid", (source) => {
        const methods = source.methods as Array<JsonObject>;
        if (methods[0]) delete methods[0].timeout;
      }],
    ];

    for (const [errorCode, mutate] of cases) {
      const source = validTestContract();
      mutate(source);
      const result = await validateFixture(source);
      expect(result.status, errorCode).toBe(1);
      expect(result.stderr, errorCode).toContain(errorCode);
    }
  });

  it("honors isolated output paths in check mode", async () => {
    const directory = await mkdtemp(path.join(os.tmpdir(), "caicli-contract-output-test-"));
    tempDirectories.push(directory);
    const fixturePath = path.join(directory, "contract.json");
    const csharpPath = path.join(directory, "Generated.g.cs");
    const typescriptPath = path.join(directory, "generated.ts");
    await writeFile(fixturePath, JSON.stringify(validTestContract()), "utf8");
    await writeFile(csharpPath, "stale-csharp", "utf8");
    await writeFile(typescriptPath, "stale-typescript", "utf8");

    const result = spawnSync(process.execPath, [
      generatorPath,
      "--check",
      "--contract",
      fixturePath,
      "--csharp-out",
      csharpPath,
      "--typescript-out",
      typescriptPath,
    ], {
      cwd: repoRoot,
      encoding: "utf8",
    });

    expect(result.status).toBe(1);
    expect(result.stderr).toContain(csharpPath);
    expect(await readFile(csharpPath, "utf8")).toBe("stale-csharp");
    expect(await readFile(typescriptPath, "utf8")).toBe("stale-typescript");
  });

  it("generates strict byte-stable C# and TypeScript contracts with one hash", async () => {
    const directory = await mkdtemp(path.join(os.tmpdir(), "caicli-contract-generation-test-"));
    tempDirectories.push(directory);
    const source = richTestContract();
    const fixturePath = path.join(directory, "contract.json");
    const csharpPath = path.join(directory, "Generated.g.cs");
    const typescriptPath = path.join(directory, "generated.ts");
    await writeFile(fixturePath, `${JSON.stringify(source, null, 2)}\n`, "utf8");
    const args = [
      generatorPath,
      "--contract",
      fixturePath,
      "--csharp-out",
      csharpPath,
      "--typescript-out",
      typescriptPath,
    ];

    const generated = spawnSync(process.execPath, args, { cwd: repoRoot, encoding: "utf8" });
    expect(generated.status, generated.stderr).toBe(0);
    const csharp = await readFile(csharpPath, "utf8");
    const typescript = await readFile(typescriptPath, "utf8");
    const hash = createHash("sha256").update(canonicalJson(source)).digest("hex");

    expect(csharp).toContain(`public const string ContractSha256 = "${hash}";`);
    expect(csharp).toContain("JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)");
    expect(csharp).toContain("public required int SchemaVersion");
    expect(csharp).toContain("public required long Count");
    expect(csharp).toContain("public required DateTimeOffset OccurredAtUtc");
    expect(csharp).toContain("public required Details? Details");
    expect(csharp).toContain("public IReadOnlyList<string>? Labels");
    expect(csharp).toContain("public static bool TryValidate(PingParams value, out string errorCode)");
    expect(typescript).toContain(`export const CONTRACT_SHA256 = "${hash}" as const;`);
    expect(typescript).toContain("readonly labels?: readonly string[];");
    expect(typescript).toContain("export function isPingParams(value: unknown): value is PingParams");
    expect(typescript).toContain("Number.isSafeInteger");
    expect(typescript).toContain('typeof value.schemaVersion === "number" && Number.isSafeInteger(value.schemaVersion)');
    expect(typescript).toContain("isUtcDateTime");
    expect(typescript).not.toContain("  return\n");
    expect(csharp).not.toContain("if (!(true))");
    expect(csharp).not.toMatch(/generated at|timestamp/iu);
    expect(typescript).not.toMatch(/generated at|timestamp/iu);

    const firstCsharp = csharp;
    const firstTypescript = typescript;
    const regenerated = spawnSync(process.execPath, args, { cwd: repoRoot, encoding: "utf8" });
    expect(regenerated.status, regenerated.stderr).toBe(0);
    expect(await readFile(csharpPath, "utf8")).toBe(firstCsharp);
    expect(await readFile(typescriptPath, "utf8")).toBe(firstTypescript);

    const checked = spawnSync(process.execPath, [...args, "--check"], {
      cwd: repoRoot,
      encoding: "utf8",
    });
    expect(checked.status, checked.stderr).toBe(0);
  });

  it("validates the checked-in Week 69 contract as a closed type graph", () => {
    const result = spawnSync(process.execPath, [
      generatorPath,
      "--check",
      "--validate-only",
      "--contract",
      contractPath,
    ], {
      cwd: repoRoot,
      encoding: "utf8",
    });

    expect(result.status, result.stderr).toBe(0);
  });

  it("keeps checked-in C# and TypeScript outputs synchronized", () => {
    const result = spawnSync(process.execPath, [generatorPath, "--check"], {
      cwd: repoRoot,
      encoding: "utf8",
    });

    expect(result.status, result.stderr).toBe(0);
  });

  it("publishes a closed meta-schema for the reviewed DSL", async () => {
    const contract = await readContract();
    const schemaPath = path.resolve(path.dirname(contractPath), "contract.schema.json");
    const schema = JSON.parse(await readFile(schemaPath, "utf8")) as JsonObject;
    const definitions = schema.$defs as JsonObject;

    expect(contractPath.endsWith("contract.json")).toBe(true);
    expect(schema.additionalProperties).toBe(false);
    expect((definitions.typeDefinition as JsonObject).additionalProperties).toBe(false);
    expect((definitions.propertyDefinition as JsonObject).additionalProperties).toBe(false);
  });

  it("validates examples for every method and notification with generated guards", async () => {
    const contract = await readContract();
    const examplesPath = path.join(path.dirname(contractPath), "examples", "methods.json");
    const examples = JSON.parse(await readFile(examplesPath, "utf8")) as {
      methods: Array<{ method: string; params: unknown; result: unknown }>;
      notifications: Array<{ method: string; params: unknown }>;
      error: unknown;
    };
    const validators = generatedContracts as unknown as Record<
      string,
      ((value: unknown) => boolean) | unknown
    >;

    expect(examples.methods.map(({ method }) => method)).toEqual(
      contract.methods.map(({ name }) => name),
    );
    for (const example of examples.methods) {
      const method = contract.methods.find(({ name }) => name === example.method);
      expect(method).toBeDefined();
      const paramsValidator = validators[`is${method?.params}`];
      const resultValidator = validators[`is${method?.result}`];
      expect(typeof paramsValidator).toBe("function");
      expect(typeof resultValidator).toBe("function");
      expect((paramsValidator as (value: unknown) => boolean)(example.params), example.method).toBe(true);
      expect((resultValidator as (value: unknown) => boolean)(example.result), example.method).toBe(true);
    }

    expect(examples.notifications.map(({ method }) => method)).toEqual(
      contract.notifications.map(({ name }) => name),
    );
    for (const example of examples.notifications) {
      const notification = contract.notifications.find(({ name }) => name === example.method);
      const validator = validators[`is${notification?.params}`];
      expect(typeof validator).toBe("function");
      expect((validator as (value: unknown) => boolean)(example.params), example.method).toBe(true);
    }
    expect(examples.error).toEqual({
      jsonrpc: "2.0",
      id: 1,
      error: {
        code: -32602,
        message: "Request parameters are invalid.",
        data: { errorCode: "invalid-params", safeMessage: "Request parameters are invalid." },
      },
    });
  });

  it("makes generator check reject invalid example values", async () => {
    const directory = await mkdtemp(path.join(os.tmpdir(), "caicli-contract-examples-test-"));
    tempDirectories.push(directory);
    const fixturePath = path.join(directory, "contract.json");
    const examplesPath = path.join(directory, "methods.json");
    await writeFile(fixturePath, JSON.stringify(validTestContract()), "utf8");
    await writeFile(examplesPath, JSON.stringify({
      methods: [{ method: "app.ping", params: { value: 42 }, result: { accepted: true } }],
      notifications: [],
      error: {
        jsonrpc: "2.0",
        id: 1,
        error: { code: -32602, message: "Invalid.", data: { errorCode: "invalid-params", safeMessage: "Invalid." } },
      },
    }), "utf8");

    const result = spawnSync(process.execPath, [
      generatorPath,
      "--check",
      "--validate-only",
      "--contract",
      fixturePath,
      "--examples",
      examplesPath,
    ], {
      cwd: repoRoot,
      encoding: "utf8",
    });

    expect(result.status).toBe(1);
    expect(result.stderr).toContain("contract-example-invalid");
  });

  it("accepts zero-offset wire timestamps and rejects normalized calendar dates", () => {
    const event = {
      schemaVersion: 1,
      eventSequence: 1,
      workspaceId: "ws_0123456789abcdef01234567",
      threadId: "thread_0123456789abcdef01234567",
      revision: 0,
      committedSequence: 0,
      changeKind: "created",
      emittedAtUtc: "2026-07-16T12:00:00+00:00",
    };

    expect(generatedContracts.isThreadChangedParams(event)).toBe(true);
    expect(generatedContracts.isThreadChangedParams({
      ...event,
      emittedAtUtc: "2026-02-31T12:00:00+00:00",
    })).toBe(false);
  });

  it("rejects contradictory application outcome envelopes", () => {
    expect(generatedContracts.isWorkspaceOpenResult({
      schemaVersion: 1,
      succeeded: true,
      data: null,
      error: null,
      diagnostics: [],
      truncated: false,
    })).toBe(false);
  });
});
