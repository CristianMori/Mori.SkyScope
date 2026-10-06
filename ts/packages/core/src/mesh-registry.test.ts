// Mori.SkyScope — The mesh registry finds a glTF's external buffer among registered sibling files and keeps the material colour for mesh markers.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { joinUri } from "./scene3d/mesh-formats.js";
import { MarkerLayer, MeshRegistry } from "./scene3d/robot-layers3d.js";
import { RecordingPainter3D } from "./scene3d/painter3d.js";
import { RecordingPainter } from "./paint/recording-painter.js";
import { Camera3D } from "./scene3d/camera3d.js";

const dir = path.resolve(fileURLToPath(new URL(".", import.meta.url)), "../../../../spec/meshes");
const load = (name: string): Uint8Array => new Uint8Array(fs.readFileSync(path.join(dir, name)));

describe("MeshRegistry with sibling files", () => {
  it("joins URIs like a URL", () => {
    expect(joinUri("package://robot/meshes/arm.gltf", "arm.bin")).toBe("package://robot/meshes/arm.bin");
    expect(joinUri("package://robot/meshes/arm.gltf", "../textures/../bin/arm.bin")).toBe("package://robot/bin/arm.bin");
    expect(joinUri("meshes/arm.gltf", "./arm.bin")).toBe("meshes/arm.bin");
    expect(joinUri("arm.gltf", "arm.bin")).toBe("arm.bin");
    expect(joinUri("package://robot/meshes/arm.gltf", "file:///tmp/arm.bin")).toBe("file:///tmp/arm.bin");
    expect(joinUri("package://robot/meshes/arm.gltf", "/abs/arm.bin")).toBe("/abs/arm.bin");
  });

  it("loads a .gltf whose buffer is a registered sibling, falls back to the resolver, and keeps the colour", () => {
    const reg = new MeshRegistry();
    expect(() => reg.load("package://robot/meshes/box-external.gltf", load("box-external.gltf"))).toThrow("gltf: cannot resolve buffer box-external.bin");
    reg.registerFiles("package://robot/meshes", { "box-external.bin": load("box-external.bin") });
    const mesh = reg.load("package://robot/meshes/box-external.gltf", load("box-external.gltf"));
    expect(mesh.positions.length / 3).toBe(8);
    expect(reg.color("package://robot/meshes/box-external.gltf")).toBe("#3399e6");
    let asked = "";
    reg.resolver = (uri) => { asked = uri; return load("box-external.bin"); };
    reg.load("package://other/box-external.glb", load("box-external.glb"));
    expect(asked).toBe("package://other/box-external.bin");
    expect(reg.color("package://other/box-external.glb")).toBe("#3399e6");
    expect(reg.color("missing")).toBeUndefined();
  });

  it("mesh markers without a colour draw with the resource's colour; a marker colour wins", () => {
    const reg = new MeshRegistry();
    reg.load("tri.gltf", load("tri.gltf"));
    const layer = new MarkerLayer("m", { meshes: reg });
    layer.setMarkers([{ id: "a", type: "mesh", meshResource: "tri.gltf" }, { id: "b", type: "mesh", meshResource: "tri.gltf", color: "#00ff00" }, { id: "c", type: "cube" }]);
    const camera = new Camera3D({ width: 100, height: 100 });
    const p3 = new RecordingPainter3D(100, 100);
    p3.begin(camera.view(), camera.projection(), null);
    layer.draw({ painter: new RecordingPainter(100, 100), painter3d: p3, projection: camera, width: 100, height: 100, now: 0, opacity: 1 });
    p3.end();
    const colors = p3.ops.filter((o) => o.op === "triangles").map((o) => (o.material as { color: string }).color);
    expect(colors).toEqual(["#cc331a", "#00ff00", "#ffffff"]);
  });
});
