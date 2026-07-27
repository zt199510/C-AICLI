const safeCategories = Object.freeze([
  "ApplicationResultEnvelope",
  "Array",
  "ArrayBufferView",
  "Closure",
  "Code",
  "DOM",
  "DateOrIntl",
  "MapOrSet",
  "NativeOther",
  "Object",
  "ObjectOther",
  "Promise",
  "ReactElement",
  "ReactFiber",
  "String",
  "Synthetic",
  "ThreadDetailProjection",
  "TimelineItemProjection",
  "TurnProjection",
  "V8Internal",
]);

const ignoredEdgeTypes = new Set(["weak"]);

export function summarizeRetainedSnapshot(snapshot, baselineLastSeenObjectId) {
  if (!Number.isSafeInteger(baselineLastSeenObjectId) || baselineLastSeenObjectId < 0) {
    throw new Error("Retained-object baseline id is invalid.");
  }
  const meta = snapshot?.snapshot?.meta;
  const nodes = snapshot?.nodes;
  const edges = snapshot?.edges;
  const strings = snapshot?.strings;
  if (!meta || !Array.isArray(nodes) || !Array.isArray(edges) || !Array.isArray(strings)) {
    throw new Error("Heap snapshot payload is invalid.");
  }
  const nodeFields = meta.node_fields;
  const edgeFields = meta.edge_fields;
  const nodeWidth = nodeFields.length;
  const edgeWidth = edgeFields.length;
  const nodeTypeIndex = requiredIndex(nodeFields, "type");
  const nodeNameIndex = requiredIndex(nodeFields, "name");
  const nodeIdIndex = requiredIndex(nodeFields, "id");
  const nodeSelfSizeIndex = requiredIndex(nodeFields, "self_size");
  const nodeEdgeCountIndex = requiredIndex(nodeFields, "edge_count");
  const nodeDetachednessIndex = nodeFields.indexOf("detachedness");
  const edgeTypeIndex = requiredIndex(edgeFields, "type");
  const edgeNameIndex = requiredIndex(edgeFields, "name_or_index");
  const edgeTargetIndex = requiredIndex(edgeFields, "to_node");
  const nodeTypes = meta.node_types[nodeTypeIndex];
  const edgeTypes = meta.edge_types[edgeTypeIndex];
  if (!Array.isArray(nodeTypes) || !Array.isArray(edgeTypes) || nodeWidth < 1 || edgeWidth < 1 || nodes.length % nodeWidth !== 0) {
    throw new Error("Heap snapshot schema is unsupported.");
  }

  const nodeCount = nodes.length / nodeWidth;
  const edgeStarts = new Uint32Array(nodeCount);
  let edgeOffset = 0;
  for (let ordinal = 0; ordinal < nodeCount; ordinal++) {
    edgeStarts[ordinal] = edgeOffset;
    const count = nodes[(ordinal * nodeWidth) + nodeEdgeCountIndex];
    if (!Number.isSafeInteger(count) || count < 0) throw new Error("Heap snapshot edge count is invalid.");
    edgeOffset += count * edgeWidth;
  }
  if (edgeOffset !== edges.length) throw new Error("Heap snapshot edge table is inconsistent.");

  const categories = new Array(nodeCount);
  for (let ordinal = 0; ordinal < nodeCount; ordinal++) {
    categories[ordinal] = classifyNode(
      ordinal,
      nodes,
      edges,
      strings,
      nodeWidth,
      edgeWidth,
      edgeStarts,
      nodeTypes,
      edgeTypes,
      nodeTypeIndex,
      nodeNameIndex,
      nodeEdgeCountIndex,
      edgeTypeIndex,
      edgeNameIndex,
    );
  }

  const aggregates = new Map(safeCategories.map((category) => [category, {
    category,
    currentCount: 0,
    currentSelfBytes: 0,
    survivingNewCount: 0,
    survivingNewSelfBytes: 0,
    survivingNewDetachedCount: 0,
  }]));
  let currentSelfBytes = 0;
  let survivingNewNodeCount = 0;
  let survivingNewSelfBytes = 0;
  const surviving = new Uint8Array(nodeCount);
  for (let ordinal = 0; ordinal < nodeCount; ordinal++) {
    const offset = ordinal * nodeWidth;
    const id = nodes[offset + nodeIdIndex];
    const selfBytes = nodes[offset + nodeSelfSizeIndex];
    const category = categories[ordinal];
    const aggregate = aggregates.get(category);
    if (!aggregate || !Number.isSafeInteger(id) || !Number.isFinite(selfBytes) || selfBytes < 0) {
      throw new Error("Heap snapshot node record is invalid.");
    }
    aggregate.currentCount++;
    aggregate.currentSelfBytes += selfBytes;
    currentSelfBytes += selfBytes;
    if (id > baselineLastSeenObjectId) {
      surviving[ordinal] = 1;
      survivingNewNodeCount++;
      survivingNewSelfBytes += selfBytes;
      aggregate.survivingNewCount++;
      aggregate.survivingNewSelfBytes += selfBytes;
      if (nodeDetachednessIndex >= 0 && (nodes[offset + nodeDetachednessIndex] ?? 0) > 0) {
        aggregate.survivingNewDetachedCount++;
      }
    }
  }

  const strongEdges = new Map();
  for (let source = 0; source < nodeCount; source++) {
    if (surviving[source] !== 1) continue;
    const edgeCount = nodes[(source * nodeWidth) + nodeEdgeCountIndex];
    const start = edgeStarts[source];
    for (let index = 0; index < edgeCount; index++) {
      const offset = start + (index * edgeWidth);
      const edgeType = edgeTypes[edges[offset + edgeTypeIndex]] ?? "unknown";
      if (ignoredEdgeTypes.has(edgeType)) continue;
      const targetOffset = edges[offset + edgeTargetIndex];
      const target = targetOffset / nodeWidth;
      if (!Number.isSafeInteger(target) || target < 0 || target >= nodeCount || surviving[target] !== 1) continue;
      const key = `${categories[source]}\0${categories[target]}`;
      strongEdges.set(key, (strongEdges.get(key) ?? 0) + 1);
    }
  }

  return {
    schemaVersion: "week80-retained-object-aggregate/v1",
    baselineLastSeenObjectId,
    snapshotNodeCount: nodeCount,
    currentSelfBytes,
    survivingNewNodeCount,
    survivingNewSelfBytes,
    categories: [...aggregates.values()]
      .filter((value) => value.survivingNewCount > 0 || value.currentCount > 0)
      .sort((left, right) => right.survivingNewSelfBytes - left.survivingNewSelfBytes),
    survivingNewStrongEdges: [...strongEdges.entries()]
      .map(([key, count]) => {
        const [from, to] = key.split("\0");
        return { from, to, count };
      })
      .sort((left, right) => right.count - left.count)
      .slice(0, 40),
    rawSnapshotPersisted: false,
    rawStringsPersisted: false,
  };
}

function classifyNode(
  ordinal,
  nodes,
  edges,
  strings,
  nodeWidth,
  edgeWidth,
  edgeStarts,
  nodeTypes,
  edgeTypes,
  nodeTypeIndex,
  nodeNameIndex,
  nodeEdgeCountIndex,
  edgeTypeIndex,
  edgeNameIndex,
) {
  const nodeOffset = ordinal * nodeWidth;
  const type = nodeTypes[nodes[nodeOffset + nodeTypeIndex]] ?? "unknown";
  const name = strings[nodes[nodeOffset + nodeNameIndex]] ?? "";
  if (type === "string" || type === "concatenated string" || type === "sliced string") return "String";
  if (type === "closure") return "Closure";
  if (type === "code") return "Code";
  if (type === "synthetic") return "Synthetic";
  if (type === "array") return "Array";
  if (type === "hidden" || type === "object shape" || type === "number" || type === "symbol" || type === "regexp") {
    return "V8Internal";
  }
  if (type === "native") return isDomName(name) ? "DOM" : "NativeOther";
  if (type !== "object") return "V8Internal";
  if (name.includes("Fiber")) return "ReactFiber";
  if (isDomName(name)) return "DOM";
  if (["ArrayBuffer", "DataView", "Uint8Array", "Uint16Array", "Uint32Array", "Int8Array", "Int16Array", "Int32Array", "Float32Array", "Float64Array"].includes(name)) {
    return "ArrayBufferView";
  }
  if (["Map", "Set", "WeakMap", "WeakSet"].includes(name)) return "MapOrSet";
  if (["Date", "DateTimeFormat", "Intl.DateTimeFormat"].includes(name)) return "DateOrIntl";
  if (name === "Promise") return "Promise";

  const properties = safePropertySignature(
    ordinal,
    nodes,
    edges,
    strings,
    nodeWidth,
    edgeWidth,
    edgeStarts,
    edgeTypes,
    nodeEdgeCountIndex,
    edgeTypeIndex,
    edgeNameIndex,
  );
  if (hasAll(properties, ["thread", "turns", "timeline", "nextSequence"])) return "ThreadDetailProjection";
  if (hasAll(properties, ["itemId", "turnId", "sequence", "timestampUtc", "type", "status", "summary", "payload"])) return "TimelineItemProjection";
  if (hasAll(properties, ["turnId", "ordinal", "timelineItemCount", "recoveryRequired"])) return "TurnProjection";
  if (hasAll(properties, ["schemaVersion", "succeeded", "data", "diagnostics", "truncated"])) return "ApplicationResultEnvelope";
  if (hasAll(properties, ["type", "key", "props", "_owner"])) return "ReactElement";
  if (hasAll(properties, ["memoizedProps", "memoizedState", "child", "sibling", "return"])) return "ReactFiber";
  return name === "Object" ? "Object" : "ObjectOther";
}

function safePropertySignature(
  ordinal,
  nodes,
  edges,
  strings,
  nodeWidth,
  edgeWidth,
  edgeStarts,
  edgeTypes,
  nodeEdgeCountIndex,
  edgeTypeIndex,
  edgeNameIndex,
) {
  const result = new Set();
  const edgeCount = nodes[(ordinal * nodeWidth) + nodeEdgeCountIndex];
  const start = edgeStarts[ordinal];
  for (let index = 0; index < edgeCount; index++) {
    const offset = start + (index * edgeWidth);
    const type = edgeTypes[edges[offset + edgeTypeIndex]] ?? "unknown";
    if (type !== "property" && type !== "internal") continue;
    const property = strings[edges[offset + edgeNameIndex]] ?? "";
    if (safeSignatureProperties.has(property)) result.add(property);
  }
  return result;
}

const safeSignatureProperties = new Set([
  "_owner", "child", "data", "diagnostics", "itemId", "key", "memoizedProps", "memoizedState",
  "nextSequence", "ordinal", "payload", "props", "recoveryRequired", "return", "schemaVersion", "sequence",
  "sibling", "status", "succeeded", "summary", "thread", "timeline", "timelineItemCount", "timestampUtc",
  "truncated", "turnId", "turns", "type",
]);

function isDomName(name) {
  return /^(?:HTML.*Element|Document|Text|Node|NodeList|DOMTokenList|CSS.*|EventListener)$/.test(name);
}

function hasAll(values, required) {
  return required.every((value) => values.has(value));
}

function requiredIndex(values, name) {
  const index = values.indexOf(name);
  if (index < 0) throw new Error(`Heap snapshot is missing required field ${name}.`);
  return index;
}
