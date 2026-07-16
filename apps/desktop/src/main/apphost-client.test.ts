import { describe, expect, it } from "vitest";
import { FrameDecoder } from "./apphost-client";

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
});

function frame(payload: Buffer): Buffer {
  return Buffer.concat([
    Buffer.from(`Content-Length: ${payload.length}\r\n\r\n`, "ascii"),
    payload,
  ]);
}
