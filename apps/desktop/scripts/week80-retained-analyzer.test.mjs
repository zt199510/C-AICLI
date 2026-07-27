import { describe, expect, it } from "vitest";
import { summarizeRetainedSnapshot } from "./week80-retained-analyzer.mjs";

describe("Week 80 retained-object analyzer", () => {
  it("classifies only surviving post-boundary objects with safe fixed categories", () => {
    const strings = [
      "", "(root)", "Object", "thread", "turns", "timeline", "nextSequence",
      "itemId", "turnId", "sequence", "timestampUtc", "type", "status", "summary", "payload",
      "FiberNode", "weak-link",
    ];
    const nodeTypes = ["synthetic", "object", "string"];
    const edgeTypes = ["property", "weak"];
    const nodeFields = ["type", "name", "id", "self_size", "edge_count", "detachedness"];
    const edgeFields = ["type", "name_or_index", "to_node"];
    const nodes = [
      0, 1, 1, 0, 3, 0,
      1, 2, 3, 24, 4, 0,
      1, 2, 11, 64, 8, 0,
      1, 15, 13, 96, 1, 1,
      2, 16, 15, 32, 0, 0,
    ];
    const offset = (ordinal) => ordinal * nodeFields.length;
    const property = (nameIndex, target) => [0, nameIndex, offset(target)];
    const edges = [
      ...property(2, 1), ...property(2, 2), ...property(15, 3),
      ...property(3, 2), ...property(4, 2), ...property(5, 2), ...property(6, 2),
      ...property(7, 4), ...property(8, 4), ...property(9, 4), ...property(10, 4),
      ...property(11, 4), ...property(12, 4), ...property(13, 4), ...property(14, 4),
      1, 16, offset(4),
    ];
    const summary = summarizeRetainedSnapshot({
      snapshot: {
        meta: {
          node_fields: nodeFields,
          node_types: [nodeTypes, "string", "number", "number", "number", "number"],
          edge_fields: edgeFields,
          edge_types: [edgeTypes, "string", "node"],
        },
      },
      nodes,
      edges,
      strings,
    }, 9);

    expect(summary.survivingNewNodeCount).toBe(3);
    expect(summary.survivingNewSelfBytes).toBe(192);
    expect(summary.categories.find((value) => value.category === "TimelineItemProjection")).toMatchObject({
      survivingNewCount: 1,
      survivingNewSelfBytes: 64,
    });
    expect(summary.categories.find((value) => value.category === "ReactFiber")).toMatchObject({
      survivingNewCount: 1,
      survivingNewSelfBytes: 96,
      survivingNewDetachedCount: 1,
    });
    expect(summary.categories.find((value) => value.category === "String")).toMatchObject({
      survivingNewCount: 1,
      survivingNewSelfBytes: 32,
    });
    expect(summary.survivingNewStrongEdges).not.toContainEqual(expect.objectContaining({ from: "ReactFiber", to: "String" }));
    expect(summary.rawSnapshotPersisted).toBe(false);
    expect(summary.rawStringsPersisted).toBe(false);
    expect(JSON.stringify(summary)).not.toContain("weak-link");
  });
});
