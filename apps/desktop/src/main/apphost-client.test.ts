import { describe, expect, it } from "vitest";
import { decodeJsonRpcResponse, FrameDecoder, parseResponse, parseServerMessage } from "./apphost-client";

describe("AppHost frame decoder", () => {
  it("reassembles partial frames and returns consecutive payloads", () => {
    const decoder = new FrameDecoder();
    const first = Buffer.from('{"id":1}', "utf8");
    const second = Buffer.from('{"id":2}', "utf8");
    const framed = Buffer.concat([frame(first), frame(second)]);

    expect(decoder.push(framed.subarray(0, 7))).toEqual([]);
    const decoded = decoder.push(framed.subarray(7));

    expect(decoded.map((payload) => payload.toString("utf8"))).toEqual([
      '{"id":1}',
      '{"id":2}',
    ]);
  });

  it("rejects oversized payload declarations", () => {
    const decoder = new FrameDecoder();
    expect(() => decoder.push(Buffer.from("Content-Length: 1048577\r\n\r\n", "ascii"))).toThrow(
      "frame-body-too-large",
    );
  });

  it.each([
    ["missing length", "Content-Type: application/vscode-jsonrpc; charset=utf-8\r\n\r\n{}", "frame-content-length-missing"],
    ["duplicate length", "Content-Length: 2\r\nContent-Length: 2\r\n\r\n{}", "frame-content-length-invalid"],
    ["unknown header", "X-Test: 1\r\nContent-Length: 2\r\n\r\n{}", "frame-header-invalid"],
    ["unsupported content type", "Content-Length: 2\r\nContent-Type: application/json\r\n\r\n{}", "frame-header-invalid"],
    ["empty body", "Content-Length: 0\r\n\r\n", "frame-body-empty"],
  ])("rejects %s", (_name, value, code) => {
    expect(() => new FrameDecoder().push(Buffer.from(value, "ascii"))).toThrow(code);
  });

  it("accepts the reviewed optional content type", () => {
    const value = "Content-Length: 2\r\nContent-Type: application/vscode-jsonrpc; charset=utf-8\r\n\r\n{}";
    expect(new FrameDecoder().push(Buffer.from(value, "ascii"))[0]?.toString()).toBe("{}");
  });

  it("rejects partial data at EOF", () => {
    const header = new FrameDecoder();
    header.push(Buffer.from("Content-Length: 2\r\n", "ascii"));
    expect(() => header.finish()).toThrow("frame-header-incomplete");

    const body = new FrameDecoder();
    body.push(Buffer.from("Content-Length: 2\r\n\r\n{", "ascii"));
    expect(() => body.finish()).toThrow("frame-body-incomplete");
  });
});

describe("AppHost response envelope", () => {
  it("rejects invalid UTF-8 and JSON", () => {
    expect(() => decodeJsonRpcResponse(Buffer.from([0xc3, 0x28]))).toThrow("frame-utf8-invalid");
    expect(() => decodeJsonRpcResponse(Buffer.from("{", "utf8"))).toThrow("protocol-invalid");
  });

  it.each([
    null,
    [],
    { jsonrpc: "1.0", id: 1, result: {} },
    { jsonrpc: "2.0", id: 0, result: {} },
    { jsonrpc: "2.0", id: 1.5, result: {} },
    { jsonrpc: "2.0", id: 1 },
    { jsonrpc: "2.0", id: 1, result: {}, error: { code: -1, message: "x" } },
    { jsonrpc: "2.0", id: 1, result: {}, extra: true },
    { jsonrpc: "2.0", id: 1, error: { code: -1, message: "x", extra: true } },
  ])("rejects invalid envelope %#", (value) => {
    expect(() => parseResponse(value)).toThrow("protocol-invalid");
  });

  it("accepts strict result and error envelopes", () => {
    expect(parseResponse({ jsonrpc: "2.0", id: 1, result: { ok: true } })).toEqual({
      jsonrpc: "2.0", id: 1, result: { ok: true },
    });
    expect(parseResponse({ jsonrpc: "2.0", id: 2, error: { code: -32000, message: "safe" } })).toEqual({
      jsonrpc: "2.0", id: 2, error: { code: -32000, message: "safe" },
    });
  });
});

describe("AppHost server message union", () => {
  const changed = {
    schemaVersion: 1,
    eventSequence: 1,
    workspaceId: "workspace-1",
    threadId: "thread-1",
    revision: 2,
    committedSequence: 0,
    changeKind: "renamed",
    emittedAtUtc: "2026-07-17T04:00:00.000Z",
  };

  it("accepts an exact generated thread notification without a response id", () => {
    expect(parseServerMessage({ jsonrpc: "2.0", method: "thread.changed", params: changed })).toEqual({
      jsonrpc: "2.0", method: "thread.changed", params: changed,
    });
  });

  it.each([
    { jsonrpc: "2.0", method: "unknown", params: changed },
    { jsonrpc: "2.0", method: "thread.changed" },
    { jsonrpc: "2.0", id: 1, method: "thread.changed", params: changed },
    { jsonrpc: "2.0", method: "thread.changed", params: changed, extra: true },
    { jsonrpc: "2.0", method: "thread.changed", params: { ...changed, eventSequence: 0 } },
    { jsonrpc: "2.0", method: "thread.changed", params: { ...changed, extra: true } },
  ])("rejects invalid notification %#", (value) => {
    expect(() => parseServerMessage(value)).toThrow("protocol-invalid");
  });
});

function frame(payload: Buffer): Buffer {
  return Buffer.concat([
    Buffer.from(`Content-Length: ${payload.length}\r\n\r\n`, "ascii"),
    payload,
  ]);
}
