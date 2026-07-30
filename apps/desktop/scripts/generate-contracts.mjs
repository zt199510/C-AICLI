import { createHash } from "node:crypto";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const desktopRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = path.resolve(desktopRoot, "../..");
const argumentValue = (name) => {
  const index = process.argv.indexOf(name);
  return index < 0 ? undefined : process.argv[index + 1];
};
const defaultContractPath = path.join(repoRoot, "protocol", "desktop-v1", "contract.json");
const contractPath = path.resolve(argumentValue("--contract") ?? defaultContractPath);
const examplesPath = argumentValue("--examples")
  ? path.resolve(argumentValue("--examples"))
  : contractPath === defaultContractPath
    ? path.join(path.dirname(contractPath), "examples", "methods.json")
    : undefined;
const csharpPath = path.resolve(
  argumentValue("--csharp-out") ?? path.join(
    repoRoot,
    "src",
    "CSharpAiCli.AppHost",
    "Protocol",
    "Generated",
    "DesktopProtocolContracts.g.cs",
  ),
);
const typescriptPath = path.resolve(
  argumentValue("--typescript-out") ??
    path.join(desktopRoot, "src", "generated", "desktop-contracts.ts"),
);
const checkOnly = process.argv.includes("--check");
const validateOnly = process.argv.includes("--validate-only");
const contract = JSON.parse(await readFile(contractPath, "utf8"));
const utcDateTimePatternSource = "^(\\d{4})-(\\d{2})-(\\d{2})T(\\d{2}):(\\d{2}):(\\d{2})(?:\\.\\d{1,7})?(?:Z|\\+00:00)$";
const utcDateTimePattern = new RegExp(utcDateTimePatternSource, "u");

function isStrictUtcDateTime(value) {
  if (typeof value !== "string") return false;
  const match = utcDateTimePattern.exec(value);
  if (match === null) return false;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const hour = Number(match[4]);
  const minute = Number(match[5]);
  const second = Number(match[6]);
  if (year < 1) return false;
  const parsed = new Date(0);
  parsed.setUTCFullYear(year, month - 1, day);
  parsed.setUTCHours(hour, minute, second, 0);
  return parsed.getUTCFullYear() === year && parsed.getUTCMonth() === month - 1 &&
    parsed.getUTCDate() === day && parsed.getUTCHours() === hour &&
    parsed.getUTCMinutes() === minute && parsed.getUTCSeconds() === second;
}

function validateContract(source) {
  const fail = (code, detail) => {
    throw new Error(`${code}: ${detail}`);
  };
  const requireObject = (value, code, detail) => {
    if (value === null || typeof value !== "object" || Array.isArray(value)) fail(code, detail);
  };
  const rejectUnknown = (value, allowed, code, detail) => {
    requireObject(value, code, detail);
    const unknown = Object.keys(value).find((key) => !allowed.has(key));
    if (unknown) fail(code, `${detail}.${unknown}`);
  };
  const safeIdentifier = (value) => typeof value === "string" && /^[A-Za-z][A-Za-z0-9]*$/u.test(value);
  const requireIdentifier = (value, detail) => {
    if (!safeIdentifier(value)) fail("contract-identifier-unsafe", detail);
  };
  const requireUnique = (values, code, detail) => {
    const seen = new Set();
    for (const value of values) {
      if (seen.has(value)) fail(code, `${detail}: ${value}`);
      seen.add(value);
    }
  };

  rejectUnknown(
    source,
    new Set(["$schema", "protocolVersion", "schemaVersion", "capabilities", "errors", "frameErrors", "limits", "methods", "notifications", "types"]),
    "contract-root-member-unknown",
    "contract",
  );
  if (typeof source.protocolVersion !== "string" || source.protocolVersion.length === 0) {
    fail("contract-version-invalid", "protocolVersion");
  }
  if (!Number.isSafeInteger(source.schemaVersion) || source.schemaVersion < 1) {
    fail("contract-schema-version-invalid", "schemaVersion");
  }
  if (!Array.isArray(source.capabilities) || !Array.isArray(source.errors) ||
      !Array.isArray(source.frameErrors) ||
      !Array.isArray(source.methods) || !Array.isArray(source.notifications) ||
      !Array.isArray(source.types)) {
    fail("contract-collection-invalid", "contract collections must be arrays");
  }
  requireObject(source.limits, "contract-limits-invalid", "limits");

  for (const capability of source.capabilities) {
    rejectUnknown(capability, new Set(["name", "required"]), "contract-capability-member-unknown", "capability");
    if (typeof capability.name !== "string" || capability.name.length === 0 ||
        typeof capability.required !== "boolean") {
      fail("contract-capability-invalid", "capability");
    }
  }
  requireUnique(source.capabilities.map(({ name }) => name), "contract-capability-duplicate", "capability");

  for (const error of source.errors) {
    rejectUnknown(error, new Set(["code", "rpcCode"]), "contract-error-member-unknown", "error");
    if (typeof error.code !== "string" || error.code.length === 0 ||
        !Number.isSafeInteger(error.rpcCode)) {
      fail("contract-error-invalid", "error");
    }
  }
  requireUnique(source.errors.map(({ code }) => code), "contract-error-duplicate", "error code");
  if (source.frameErrors.some((code) => typeof code !== "string" ||
      !/^frame-[a-z0-9]+(?:-[a-z0-9]+)*$/u.test(code))) {
    fail("contract-frame-error-invalid", "frame error");
  }
  requireUnique(source.frameErrors, "contract-frame-error-duplicate", "frame error");

  for (const [name, value] of Object.entries(source.limits)) {
    requireIdentifier(name, `limit ${name}`);
    if (!Number.isSafeInteger(value) || value < 1) fail("contract-limit-invalid", name);
  }

  for (const method of source.methods) {
    rejectUnknown(
      method,
      new Set(["constant", "name", "params", "result", "internal", "requiresWorkspace", "mutation", "timeout"]),
      "contract-method-member-unknown",
      "method",
    );
    requireIdentifier(method.constant, `method constant ${method.constant}`);
    requireIdentifier(method.params, `method params ${method.params}`);
    requireIdentifier(method.result, `method result ${method.result}`);
    if (typeof method.name !== "string" || !/^[a-z][a-z0-9]*(?:\.[a-z][a-z0-9]*)+$/u.test(method.name)) {
      fail("contract-method-name-invalid", String(method.name));
    }
    if (typeof method.requiresWorkspace !== "boolean" || typeof method.mutation !== "boolean" ||
        !["initialize", "query", "mutation", "shutdown"].includes(method.timeout)) {
      fail("contract-method-metadata-invalid", method.name);
    }
  }
  requireUnique(source.methods.map(({ constant }) => constant), "contract-constant-duplicate", "method constant");
  requireUnique(source.methods.map(({ name }) => name), "contract-method-duplicate", "method");

  for (const notification of source.notifications) {
    rejectUnknown(
      notification,
      new Set(["constant", "name", "params", "capability"]),
      "contract-notification-member-unknown",
      "notification",
    );
    requireIdentifier(notification.constant, `notification constant ${notification.constant}`);
    requireIdentifier(notification.params, `notification params ${notification.params}`);
    if (typeof notification.name !== "string" ||
        !/^[a-z][a-z0-9]*(?:\.[a-z][a-z0-9]*)+$/u.test(notification.name)) {
      fail("contract-notification-name-invalid", String(notification.name));
    }
  }
  requireUnique(
    [...source.methods, ...source.notifications].map(({ constant }) => constant),
    "contract-constant-duplicate",
    "protocol constant",
  );
  requireUnique(source.notifications.map(({ name }) => name), "contract-notification-duplicate", "notification");

  for (const type of source.types) requireIdentifier(type?.name, `type ${type?.name}`);
  requireUnique(source.types.map(({ name }) => name), "contract-type-duplicate", "type");
  const typeNames = new Set(source.types.map(({ name }) => name));
  const scalarTypes = new Set(["string", "boolean", "int32", "int64", "datetime"]);
  const edges = new Map(source.types.map(({ name }) => [name, []]));

  for (const type of source.types) {
    rejectUnknown(type, new Set(["name", "properties"]), "contract-type-member-unknown", type.name);
    if (!Array.isArray(type.properties)) fail("contract-properties-invalid", type.name);
    for (const property of type.properties) {
      rejectUnknown(
        property,
        new Set(["jsonName", "type", "items", "maxItems", "maxUtf8Bytes", "enum", "min", "max", "nullable", "optional"]),
        "contract-property-member-unknown",
        type.name,
      );
      requireIdentifier(property.jsonName, `${type.name}.${property.jsonName}`);
    }
    requireUnique(type.properties.map(({ jsonName }) => jsonName), "contract-property-duplicate", type.name);

    for (const property of type.properties) {
      const detail = `${type.name}.${property.jsonName}`;
      if (property.nullable !== undefined && typeof property.nullable !== "boolean") {
        fail("contract-nullable-invalid", detail);
      }
      if (property.optional !== undefined && typeof property.optional !== "boolean") {
        fail("contract-optional-invalid", detail);
      }
      if (property.type === "array") {
        if (!Number.isSafeInteger(property.maxItems) || property.maxItems < 1) {
          fail("contract-array-unbounded", detail);
        }
        if (!scalarTypes.has(property.items) && !typeNames.has(property.items)) {
          fail("contract-type-reference-missing", `${detail}.${property.items}`);
        }
        if (property.items === "string" &&
            (!Number.isSafeInteger(property.maxUtf8Bytes) || property.maxUtf8Bytes < 1)) {
          fail("contract-string-unbounded", `${detail} items require maxUtf8Bytes`);
        }
        if ((property.items === "int32" || property.items === "int64") &&
            (!Number.isSafeInteger(property.min) || !Number.isSafeInteger(property.max) ||
             property.min > property.max)) {
          fail("contract-number-unbounded", `${detail} items`);
        }
        if (typeNames.has(property.items)) edges.get(type.name).push(property.items);
        continue;
      }
      if (!scalarTypes.has(property.type) && !typeNames.has(property.type)) {
        fail("contract-type-reference-missing", `${detail}.${property.type}`);
      }
      if (typeNames.has(property.type)) edges.get(type.name).push(property.type);
      if (property.type === "string") {
        if (!Number.isSafeInteger(property.maxUtf8Bytes) || property.maxUtf8Bytes < 1) {
          fail("contract-string-unbounded", `${detail} requires maxUtf8Bytes`);
        }
        if (property.enum !== undefined &&
            (!Array.isArray(property.enum) || property.enum.length === 0 ||
             property.enum.some((value) => typeof value !== "string") ||
             new Set(property.enum).size !== property.enum.length ||
             property.enum.some((value) => Buffer.byteLength(value, "utf8") > property.maxUtf8Bytes))) {
          fail("contract-enum-invalid", detail);
        }
      }
      if (property.type === "int32" || property.type === "int64") {
        if (!Number.isSafeInteger(property.min) || !Number.isSafeInteger(property.max) ||
            property.min > property.max) {
          fail("contract-number-unbounded", detail);
        }
      }
    }
  }

  for (const method of source.methods) {
    if (!typeNames.has(method.params) || !typeNames.has(method.result)) {
      fail("contract-type-reference-missing", method.name);
    }
  }
  for (const notification of source.notifications) {
    if (!typeNames.has(notification.params)) {
      fail("contract-type-reference-missing", notification.name);
    }
  }

  const visiting = new Set();
  const visited = new Set();
  const visit = (name) => {
    if (visiting.has(name)) fail("contract-object-cycle", name);
    if (visited.has(name)) return;
    visiting.add(name);
    for (const edge of edges.get(name)) visit(edge);
    visiting.delete(name);
    visited.add(name);
  };
  for (const name of typeNames) visit(name);
}

validateContract(contract);

function validateExamples(source, examples) {
  const invalid = (detail) => {
    throw new Error(`contract-example-invalid: ${detail}`);
  };
  const isObject = (value) => value !== null && typeof value === "object" && !Array.isArray(value);
  const exactKeys = (value, required, optional = []) => {
    if (!isObject(value)) return false;
    const keys = Object.keys(value);
    return required.every((key) => Object.prototype.hasOwnProperty.call(value, key)) &&
      keys.every((key) => required.includes(key) || optional.includes(key));
  };
  const typeMap = new Map(source.types.map((type) => [type.name, type]));
  const validateType = (typeName, value, detail) => {
    const type = typeMap.get(typeName);
    if (!type || !isObject(value)) invalid(detail);
    const required = type.properties.filter((property) => !property.optional).map(({ jsonName }) => jsonName);
    const optional = type.properties.filter((property) => property.optional).map(({ jsonName }) => jsonName);
    if (!exactKeys(value, required, optional)) invalid(detail);
    for (const property of type.properties) {
      if (!Object.prototype.hasOwnProperty.call(value, property.jsonName)) continue;
      const propertyValue = value[property.jsonName];
      const propertyDetail = `${detail}.${property.jsonName}`;
      if (propertyValue === null) {
        if (!property.nullable) invalid(propertyDetail);
        continue;
      }
      if (property.type === "string") {
        if (typeof propertyValue !== "string" ||
            Buffer.byteLength(propertyValue, "utf8") > property.maxUtf8Bytes ||
            (property.enum && !property.enum.includes(propertyValue))) invalid(propertyDetail);
      } else if (property.type === "boolean") {
        if (typeof propertyValue !== "boolean") invalid(propertyDetail);
      } else if (property.type === "int32" || property.type === "int64") {
        if (typeof propertyValue !== "number" || !Number.isSafeInteger(propertyValue) ||
            propertyValue < property.min || propertyValue > property.max) invalid(propertyDetail);
      } else if (property.type === "datetime") {
        if (!isStrictUtcDateTime(propertyValue)) invalid(propertyDetail);
      } else if (property.type === "array") {
        if (!Array.isArray(propertyValue) || propertyValue.length > property.maxItems) invalid(propertyDetail);
        for (const item of propertyValue) {
          if (property.items === "string") {
            if (typeof item !== "string" || Buffer.byteLength(item, "utf8") > property.maxUtf8Bytes) {
              invalid(propertyDetail);
            }
          } else if (property.items === "boolean") {
            if (typeof item !== "boolean") invalid(propertyDetail);
          } else if (property.items === "int32" || property.items === "int64") {
            if (typeof item !== "number" || !Number.isSafeInteger(item) ||
                item < property.min || item > property.max) invalid(propertyDetail);
          } else if (property.items === "datetime") {
            if (!isStrictUtcDateTime(item)) invalid(propertyDetail);
          } else {
            validateType(property.items, item, propertyDetail);
          }
        }
      } else {
        validateType(property.type, propertyValue, propertyDetail);
      }
    }
  };

  if (!exactKeys(examples, ["methods", "notifications", "error"]) ||
      !Array.isArray(examples.methods) || !Array.isArray(examples.notifications)) invalid("manifest");
  if (examples.methods.length !== source.methods.length ||
      examples.notifications.length !== source.notifications.length) invalid("coverage");
  const expectedContractSha256 = createHash("sha256").update(canonicalJson(source)).digest("hex");
  source.methods.forEach((method, index) => {
    const example = examples.methods[index];
    if (!exactKeys(example, ["method", "params", "result"]) || example.method !== method.name) {
      invalid(`method[${index}]`);
    }
    validateType(method.params, example.params, `${method.name}.params`);
    validateType(method.result, example.result, `${method.name}.result`);
    if (method.name === "app.initialize" &&
        (example.params.protocolVersion !== source.protocolVersion ||
         example.params.contractSha256 !== expectedContractSha256 ||
         example.result.protocolVersion !== source.protocolVersion ||
         example.result.contractSha256 !== expectedContractSha256)) {
      invalid("app.initialize.contract-identity");
    }
  });
  source.notifications.forEach((notification, index) => {
    const example = examples.notifications[index];
    if (!exactKeys(example, ["method", "params"]) || example.method !== notification.name) {
      invalid(`notification[${index}]`);
    }
    validateType(notification.params, example.params, `${notification.name}.params`);
  });
  if (!isObject(examples.error)) invalid("error");
}

if (examplesPath) {
  const examples = JSON.parse(await readFile(examplesPath, "utf8"));
  validateExamples(contract, examples);
}

const pascal = (value) => value.charAt(0).toUpperCase() + value.slice(1);
const pascalIdentifier = (value) => value
  .split(/[^A-Za-z0-9]+/u)
  .filter(Boolean)
  .map(pascal)
  .join("");

function canonicalJson(value) {
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(",")}]`;
  if (value !== null && typeof value === "object") {
    return `{${Object.entries(value)
      .sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0)
      .map(([key, entry]) => `${JSON.stringify(key)}:${canonicalJson(entry)}`)
      .join(",")}}`;
  }
  return JSON.stringify(value);
}

const contractSha256 = createHash("sha256").update(canonicalJson(contract)).digest("hex");
const sourceLabel = path.relative(repoRoot, contractPath).replaceAll("\\", "/");

function csharpScalarType(type) {
  if (type === "string") return "string";
  if (type === "boolean") return "bool";
  if (type === "int32") return "int";
  if (type === "int64") return "long";
  if (type === "datetime") return "DateTimeOffset";
  return type;
}

function csharpPropertyType(property) {
  const base = property.type === "array"
    ? `IReadOnlyList<${csharpScalarType(property.items)}>`
    : csharpScalarType(property.type);
  return property.nullable || property.optional ? `${base}?` : base;
}

function typescriptScalarType(type) {
  if (type === "string" || type === "datetime") return "string";
  if (type === "boolean") return "boolean";
  if (type === "int32" || type === "int64") return "number";
  return type;
}

function typescriptPropertyType(property) {
  const base = property.type === "array"
    ? `readonly ${typescriptScalarType(property.items)}[]`
    : typescriptScalarType(property.type);
  return property.nullable ? `${base} | null` : base;
}

function isApplicationOutcomeType(type) {
  const properties = new Map(type.properties.map((property) => [property.jsonName, property]));
  return properties.size === 6 && properties.has("schemaVersion") &&
    properties.get("succeeded")?.type === "boolean" &&
    properties.get("data")?.nullable === true &&
    properties.get("error")?.type === "ApplicationErrorData" &&
    properties.get("error")?.nullable === true &&
    properties.get("diagnostics")?.type === "array" &&
    properties.get("truncated")?.type === "boolean";
}

function csharpPositiveCondition(property, access) {
  let condition;
  if (property.type === "string") {
    const enumCondition = property.enum
      ? ` && ${access} is (${property.enum.map(JSON.stringify).join(" or ")})`
      : "";
    condition = `${access} is not null && Encoding.UTF8.GetByteCount(${access}) <= ${property.maxUtf8Bytes}${enumCondition}`;
  } else if (property.type === "boolean") {
    condition = "true";
  } else if (property.type === "int32" || property.type === "int64") {
    const value = property.nullable || property.optional ? `${access}.Value` : access;
    condition = `${value} >= ${property.min} && ${value} <= ${property.max}`;
  } else if (property.type === "datetime") {
    const value = property.nullable || property.optional ? `${access}.Value` : access;
    condition = `${value}.Offset == TimeSpan.Zero`;
  } else if (property.type === "array") {
    const itemCondition = property.items === "string"
      ? `item is not null && Encoding.UTF8.GetByteCount(item) <= ${property.maxUtf8Bytes}`
      : property.items === "int32" || property.items === "int64"
        ? `item >= ${property.min} && item <= ${property.max}`
        : property.items === "datetime"
          ? "item.Offset == TimeSpan.Zero"
          : `TryValidate(item, out _)`;
    condition = `${access} is not null && ${access}.Count <= ${property.maxItems} && ${access}.All(item => ${itemCondition})`;
  } else {
    condition = `${access} is not null && TryValidate(${access}, out _)`;
  }
  if (property.optional) {
    const specified = `${access}Specified`;
    return property.nullable
      ? `!${specified} || ${access} is null || (${condition})`
      : `!${specified} || (${access} is not null && (${condition}))`;
  }
  return property.nullable ? `${access} is null || (${condition})` : condition;
}

function typescriptPositiveCondition(property, access) {
  let condition;
  if (property.type === "string") {
    const enumCondition = property.enum
      ? ` && [${property.enum.map(JSON.stringify).join(", ")}].includes(${access})`
      : "";
    condition = `typeof ${access} === "string" && utf8ByteLength(${access}) <= ${property.maxUtf8Bytes}${enumCondition}`;
  } else if (property.type === "boolean") {
    condition = `typeof ${access} === "boolean"`;
  } else if (property.type === "int32" || property.type === "int64") {
    condition = `typeof ${access} === "number" && Number.isSafeInteger(${access}) && ${access} >= ${property.min} && ${access} <= ${property.max}`;
  } else if (property.type === "datetime") {
    condition = `isUtcDateTime(${access})`;
  } else if (property.type === "array") {
    const itemCondition = property.items === "string"
      ? `typeof item === "string" && utf8ByteLength(item) <= ${property.maxUtf8Bytes}`
      : property.items === "boolean"
        ? `typeof item === "boolean"`
        : property.items === "int32" || property.items === "int64"
          ? `typeof item === "number" && Number.isSafeInteger(item) && item >= ${property.min} && item <= ${property.max}`
          : property.items === "datetime"
            ? "isUtcDateTime(item)"
            : `is${property.items}(item)`;
    condition = `Array.isArray(${access}) && ${access}.length <= ${property.maxItems} && ${access}.every((item) => ${itemCondition})`;
  } else {
    condition = `is${property.type}(${access})`;
  }
  return property.nullable ? `${access} === null || (${condition})` : condition;
}

function renderCsharp() {
  const lines = [
    "// <auto-generated />",
    `// Source: ${sourceLabel}`,
    `// Protocol: ${contract.protocolVersion}`,
    `// Contract SHA256: ${contractSha256}`,
    "#nullable enable",
    "using System.Text;",
    "using System.Text.Json.Serialization;",
    "",
    "namespace CSharpAiCli.AppHost.Protocol.Generated;",
    "",
    "public static class DesktopProtocolDefinition",
    "{",
    `    public const string Version = ${JSON.stringify(contract.protocolVersion)};`,
    `    public const int SchemaVersion = ${contract.schemaVersion};`,
    `    public const string ContractSha256 = ${JSON.stringify(contractSha256)};`,
  ];
  for (const [name, value] of Object.entries(contract.limits)) {
    const type = value > 2_147_483_647 ? "long" : "int";
    const suffix = type === "long" ? "L" : "";
    lines.push(`    public const ${type} ${pascal(name)} = ${value}${suffix};`);
  }
  for (const method of contract.methods) {
    lines.push(`    public const string ${method.constant} = ${JSON.stringify(method.name)};`);
  }
  for (const notification of contract.notifications) {
    lines.push(`    public const string ${notification.constant} = ${JSON.stringify(notification.name)};`);
  }
  for (const capability of contract.capabilities) {
    lines.push(`    public const string ${pascalIdentifier(capability.name)}Capability = ${JSON.stringify(capability.name)};`);
  }
  for (const error of contract.errors) {
    const name = pascalIdentifier(error.code);
    lines.push(`    public const string ${name}Error = ${JSON.stringify(error.code)};`);
    lines.push(`    public const int ${name}RpcCode = ${error.rpcCode};`);
  }
  for (const error of contract.frameErrors) {
    lines.push(`    public const string ${pascalIdentifier(error)}Error = ${JSON.stringify(error)};`);
  }
  lines.push("", "    public static IReadOnlyList<string> Methods { get; } =", "    [");
  for (const method of contract.methods) lines.push(`        ${method.constant},`);
  lines.push("    ];", "", "    public static IReadOnlyList<string> Notifications { get; } =", "    [");
  for (const notification of contract.notifications) lines.push(`        ${notification.constant},`);
  lines.push("    ];", "", "    public static IReadOnlyList<string> Capabilities { get; } =", "    [");
  for (const capability of contract.capabilities) {
    lines.push(`        ${pascalIdentifier(capability.name)}Capability,`);
  }
  lines.push("    ];", "", "    public static IReadOnlyList<string> RequiredCapabilities { get; } =", "    [");
  for (const capability of contract.capabilities.filter(({ required }) => required)) {
    lines.push(`        ${pascalIdentifier(capability.name)}Capability,`);
  }
  lines.push("    ];", "", "    public static bool RequiresWorkspace(string? method) => method switch", "    {");
  for (const method of contract.methods.filter(({ requiresWorkspace }) => requiresWorkspace)) {
    lines.push(`        ${method.constant} => true,`);
  }
  lines.push("        _ => false", "    };", "", "    public static bool IsMutation(string? method) => method switch", "    {");
  for (const method of contract.methods.filter(({ mutation }) => mutation)) {
    lines.push(`        ${method.constant} => true,`);
  }
  lines.push("        _ => false", "    };", "", "    public static string TimeoutClass(string? method) => method switch", "    {");
  for (const method of contract.methods) {
    lines.push(`        ${method.constant} => ${JSON.stringify(method.timeout)},`);
  }
  lines.push("        _ => \"query\"", "    };", "}", "");

  for (const type of contract.types) {
    lines.push("[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]");
    lines.push(`public sealed record ${type.name}`, "{");
    for (const property of type.properties) {
      const required = property.optional ? "" : "required ";
      const propertyName = pascal(property.jsonName);
      if (property.optional) {
        lines.push(`    private ${csharpPropertyType(property)} _${property.jsonName};`, "");
      }
      lines.push(`    [JsonPropertyName(${JSON.stringify(property.jsonName)})]`);
      if (property.optional) {
        lines.push(`    public ${csharpPropertyType(property)} ${propertyName}`);
        lines.push("    {");
        lines.push(`        get => _${property.jsonName};`);
        lines.push(`        init { _${property.jsonName} = value; ${propertyName}Specified = true; }`);
        lines.push("    }", "");
        lines.push("    [JsonIgnore]");
        lines.push(`    public bool ${propertyName}Specified { get; private set; }`, "");
      } else {
        lines.push(`    public ${required}${csharpPropertyType(property)} ${propertyName} { get; init; }`, "");
      }
    }
    lines.push("}", "");
  }

  lines.push("public static class DesktopProtocolValidation", "{");
  for (const type of contract.types) {
    lines.push(`    public static bool TryValidate(${type.name} value, out string errorCode)`, "    {");
    lines.push("        if (value is null)", "        {", "            errorCode = DesktopProtocolDefinition.InvalidParamsError;", "            return false;", "        }", "");
    for (const property of type.properties) {
      if (property.type === "boolean" && !property.optional) continue;
      const access = `value.${pascal(property.jsonName)}`;
      lines.push(`        if (!(${csharpPositiveCondition(property, access)}))`);
      lines.push("        {", "            errorCode = DesktopProtocolDefinition.InvalidParamsError;", "            return false;", "        }", "");
    }
    if (isApplicationOutcomeType(type)) {
      lines.push("        if (!(value.Succeeded");
      lines.push("            ? value.Data is not null && value.Error is null");
      lines.push("            : value.Data is null && value.Error is not null))");
      lines.push("        {", "            errorCode = DesktopProtocolDefinition.InvalidParamsError;", "            return false;", "        }", "");
    }
    lines.push("        errorCode = string.Empty;", "        return true;", "    }", "");
  }
  lines.push("}");

  return `${lines.join("\n").trimEnd()}\n`;
}

function renderTypescript() {
  const lines = [
    "// <auto-generated />",
    `// Source: ${sourceLabel}`,
    `// Protocol: ${contract.protocolVersion}`,
    `// Contract SHA256: ${contractSha256}`,
    `export const PROTOCOL_VERSION = ${JSON.stringify(contract.protocolVersion)} as const;`,
    `export const SCHEMA_VERSION = ${contract.schemaVersion} as const;`,
    `export const CONTRACT_SHA256 = ${JSON.stringify(contractSha256)} as const;`,
    "export const PROTOCOL_LIMITS = Object.freeze({",
  ];
  for (const [name, value] of Object.entries(contract.limits)) {
    lines.push(`  ${name}: ${value},`);
  }
  lines.push(
    "});",
    "export const DESKTOP_METHODS = Object.freeze({",
  );
  for (const method of contract.methods) {
    lines.push(`  ${method.constant}: ${JSON.stringify(method.name)},`);
  }
  lines.push("});", "export const DESKTOP_METHOD_METADATA = Object.freeze({");
  for (const method of contract.methods) {
    lines.push(`  ${method.constant}: Object.freeze({ requiresWorkspace: ${method.requiresWorkspace}, mutation: ${method.mutation}, timeout: ${JSON.stringify(method.timeout)} }),`);
  }
  lines.push("});", "export const DESKTOP_NOTIFICATIONS = Object.freeze({");
  for (const notification of contract.notifications) {
    lines.push(`  ${notification.constant}: ${JSON.stringify(notification.name)},`);
  }
  lines.push("});", "export const DESKTOP_CAPABILITIES = Object.freeze({");
  for (const capability of contract.capabilities) {
    lines.push(`  ${pascalIdentifier(capability.name)}: ${JSON.stringify(capability.name)},`);
  }
  lines.push("});", "export const DESKTOP_ERRORS = Object.freeze({");
  for (const error of contract.errors) {
    lines.push(`  ${pascalIdentifier(error.code)}: Object.freeze({ code: ${JSON.stringify(error.code)}, rpcCode: ${error.rpcCode} }),`);
  }
  lines.push("});", "export const DESKTOP_FRAME_ERRORS = Object.freeze({");
  for (const error of contract.frameErrors) {
    lines.push(`  ${pascalIdentifier(error)}: ${JSON.stringify(error)},`);
  }
  lines.push("});", "export const DESKTOP_REQUIRED_CAPABILITIES = Object.freeze([");
  for (const capability of contract.capabilities.filter(({ required }) => required)) {
    lines.push(`  ${JSON.stringify(capability.name)},`);
  }
  lines.push("]);", "");

  for (const type of contract.types) {
    lines.push(`export interface ${type.name} {`);
    for (const property of type.properties) {
      const optional = property.optional ? "?" : "";
      lines.push(`  readonly ${property.jsonName}${optional}: ${typescriptPropertyType(property)};`);
    }
    lines.push("}", "");
  }

  lines.push(
    "const textEncoder = new TextEncoder();",
    "const utf8ByteLength = (value: string): number => textEncoder.encode(value).byteLength;",
    "const isRecord = (value: unknown): value is Record<string, unknown> =>",
    "  value !== null && typeof value === \"object\" && !Array.isArray(value);",
    "const hasOnlyKeys = (",
    "  value: Record<string, unknown>,",
    "  required: readonly string[],",
    "  optional: readonly string[],",
    "): boolean => required.every((key) => Object.prototype.hasOwnProperty.call(value, key)) &&",
    "  Object.keys(value).every((key) => required.includes(key) || optional.includes(key));",
    `const utcDateTimePattern = new RegExp(${JSON.stringify(utcDateTimePatternSource)}, "u");`,
    "const isUtcDateTime = (value: unknown): value is string => {",
    "  if (typeof value !== \"string\") return false;",
    "  const match = utcDateTimePattern.exec(value);",
    "  if (match === null) return false;",
    "  const year = Number(match[1]);",
    "  const month = Number(match[2]);",
    "  const day = Number(match[3]);",
    "  const hour = Number(match[4]);",
    "  const minute = Number(match[5]);",
    "  const second = Number(match[6]);",
    "  if (year < 1) return false;",
    "  const parsed = new Date(0);",
    "  parsed.setUTCFullYear(year, month - 1, day);",
    "  parsed.setUTCHours(hour, minute, second, 0);",
    "  return parsed.getUTCFullYear() === year && parsed.getUTCMonth() === month - 1 &&",
    "    parsed.getUTCDate() === day && parsed.getUTCHours() === hour &&",
    "    parsed.getUTCMinutes() === minute && parsed.getUTCSeconds() === second;",
    "};",
    "",
  );

  for (const type of contract.types) {
    const required = type.properties.filter((property) => !property.optional).map(({ jsonName }) => jsonName);
    const optional = type.properties.filter((property) => property.optional).map(({ jsonName }) => jsonName);
    lines.push(`export function is${type.name}(value: unknown): value is ${type.name} {`);
    lines.push(`  if (!isRecord(value) || !hasOnlyKeys(value, ${JSON.stringify(required)}, ${JSON.stringify(optional)})) return false;`);
    if (type.properties.length === 0) {
      lines.push("  return true;");
    } else {
      const conditions = type.properties.map((property) => {
        const access = `value.${property.jsonName}`;
        const condition = typescriptPositiveCondition(property, access);
        return property.optional
          ? `(!Object.prototype.hasOwnProperty.call(value, ${JSON.stringify(property.jsonName)}) || (${condition}))`
          : `(${condition})`;
      });
      if (isApplicationOutcomeType(type)) {
        conditions.push("(value.succeeded ? value.data !== null && value.error === null : value.data === null && value.error !== null)");
      }
      lines.push("  return (");
      conditions.forEach((condition, index) => {
        const suffix = index === conditions.length - 1 ? ");" : " &&";
        lines.push(`    ${condition}${suffix}`);
      });
    }
    lines.push("}", "");
  }

  return `${lines.join("\n").trimEnd()}\n`;
}

async function emit(filePath, expected) {
  if (checkOnly) {
    const actual = await readFile(filePath, "utf8").catch(() => "");
    if (actual.replaceAll("\r\n", "\n") !== expected.replaceAll("\r\n", "\n")) {
      throw new Error(`Generated contract is stale: ${filePath}`);
    }
    return;
  }

  await mkdir(path.dirname(filePath), { recursive: true });
  await writeFile(filePath, expected, "utf8");
}

if (!validateOnly) {
  await emit(csharpPath, renderCsharp());
  await emit(typescriptPath, renderTypescript());
}
