// Mori.SkyScope — In-process synthetic 3D scene for the React sample, including a two-joint arm on the robot.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { useState } from "react";
import { AxesLayer, Grid3DLayer, MarkerLayer, Path3DLayer, PointCloud3DLayer, Pose3DLayer, SceneLayerSink, parseUrdf, publishUrdf, quatFromEuler, type FanoutLayerSink, type Scene3DTool } from "@cmori/skyscope-core";

/** A two-joint arm (the same model the demo server carries) mounted on the robot. */
const ARM_URDF = `<robot name="arm">
  <material name="steel"><color rgba="0.75 0.75 0.8 1"/></material>
  <link name="base_link"><visual><origin xyz="0 0 0.1"/><geometry><cylinder radius="0.08" length="0.2"/></geometry><material name="steel"/></visual></link>
  <link name="upper_arm"><visual><origin xyz="0.2 0 0" rpy="0 1.5707963 0"/><geometry><cylinder radius="0.04" length="0.4"/></geometry><material><color rgba="0.96 0.62 0.04 1"/></material></visual></link>
  <link name="forearm"><visual><origin xyz="0.15 0 0" rpy="0 1.5707963 0"/><geometry><cylinder radius="0.03" length="0.3"/></geometry><material><color rgba="0.23 0.51 0.96 1"/></material></visual><visual><origin xyz="0.3 0 0"/><geometry><sphere radius="0.05"/></geometry><material><color rgba="0.86 0.15 0.15 1"/></material></visual></link>
  <joint name="shoulder" type="revolute"><parent link="base_link"/><child link="upper_arm"/><origin xyz="0 0 0.2"/><axis xyz="0 1 0"/><limit lower="-1.57" upper="1.57"/></joint>
  <joint name="elbow" type="revolute"><parent link="upper_arm"/><child link="forearm"/><origin xyz="0.4 0 0"/><axis xyz="0 1 0"/><limit lower="-2.5" upper="2.5"/></joint>
</robot>`;
import { Button, Scene3DView } from "@cmori/skyscope-react";

/**
 * A 3D scene generated in the browser (no server needed): a robot driving a circle on a metric grid with a
 * spinning lidar cloud, its trail, a labelled pose and a few markers. Relayed 3D layers (server-side
 * `SyntheticSceneSource3D`, ROS 2) join the same scene through `layers`.
 */
/** `?points=1000000` sizes the synthetic cloud (default 720) and `?rate=1000` slows its regeneration — the browser bench; the frame rate shows in the toolbar. */
export function Scene3DDemo({ layers, dark }: { layers: FanoutLayerSink; dark: boolean }) {
  const [tool, setTool] = useState<Scene3DTool>("orbit");
  const [fps, setFps] = useState(0);
  // ---- bench knobs from the query string ----
  const params = new URLSearchParams(location.search);
  const points = Math.max(4, Number(params.get("points")) || 720);
  /** Cloud update period in ms (`?rate=1000` regenerates once a second: the renderer alone is measured). */
  const rate = Math.max(10, Number(params.get("rate")) || 50);
  /** Initial camera distance in metres (`?dist=4` frames the robot and its arm). */
  const dist = Number(params.get("dist")) || 14;
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 4, minHeight: 0, height: "100%" }}>
      <div style={{ display: "flex", gap: 4 }}>
        {(["orbit", "measure", "select"] as Scene3DTool[]).map((t) => <Button key={t} size="sm" active={tool === t} onClick={() => setTool(t)}>{t}</Button>)}
        <span style={{ fontSize: 11, alignSelf: "center", color: "var(--skyscope-color-text-secondary)" }}>drag orbit · middle/shift pan · wheel dolly · F fit · T top · O ortho · {points.toLocaleString()} pts · {fps} fps</span>
      </div>
      <div style={{ flex: 1, minHeight: 0, border: "1px solid var(--skyscope-color-border)", borderRadius: 4, overflow: "hidden" }}>
        <Scene3DView key={dark ? "d" : "l"} tool={tool} options={{ animate: true, fixedFrame: "map", camera: { distance: dist, pitch: 0.7, yaw: -2.2 }, theme: dark ? {} : { background: "#e2e8f0", overlay: "#0f172a", overlayText: "#0f172a" } }} onReady={(c, v) => {
          // ---- static scene: frame tree (map > base_link > laser), grid, axes, and the layers the timer animates ----
          const frames = c.frames;
          frames.set("laser", "base_link", { t: { x: 0.2, y: 0, z: 0.35 }, q: { x: 0, y: 0, z: 0, w: 1 } });
          c.scene.add(new Grid3DLayer("grid", { size: 8, spacing: 1, color: dark ? "#1e293b" : "#94a3b8", majorColor: dark ? "#475569" : "#64748b" }));
          c.scene.add(new AxesLayer("axes", { frames, fixedFrame: "map", length: 0.6 }));
          const cloud = new PointCloud3DLayer("lidar", { frame: "laser", frames, fixedFrame: "map", colormap: "turbo", pointSize: 3 });
          const trail = new Path3DLayer("trail", { frame: "map", frames, fixedFrame: "map", color: "#22d3ee", maxPoints: 600 });
          const pose = new Pose3DLayer("robot", { frame: "base_link", frames, fixedFrame: "map", label: "robot", color: "#f59e0b", axisLength: 0.8 });
          const markers = new MarkerLayer("markers", { frames, fixedFrame: "map" });
          // one marker of each primitive type, in map coordinates (metres)
          markers.setMarkers([
            { id: "dock", type: "cube", position: [4, 4, 0.25], scale: [1, 0.6, 0.5], color: "#3b82f6" },
            { id: "dock-label", type: "text", position: [4, 4, 0.9], text: "dock", color: dark ? "#e2e8f0" : "#0f172a" },
            { id: "goal", type: "sphere", position: [-3, 2, 0.3], scale: [0.6, 0.6, 0.6], color: "#22c55e", opacity: 0.7 },
            { id: "pillar", type: "cylinder", position: [0, -4, 1], scale: [0.5, 0.5, 2], color: "#a855f7" },
            { id: "heading", type: "arrow", position: [-3, 2, 0.3], orientation: [0, 0, 0.3826834, 0.9238795], scale: [1.2, 0.15, 0.15], color: "#eab308" },
            { id: "fence", type: "lineStrip", points: [-5, -5, 0, 5, -5, 0, 5, 5, 0, -5, 5, 0, -5, -5, 0], scale: [1, 0, 0], color: "#f87171" },
          ]);
          c.scene.add(cloud); c.scene.add(trail); c.scene.add(pose); c.scene.add(markers);
          // the URDF arm goes in through the layer contract, like a robot description from ROS 2 would
          const local = new SceneLayerSink(c.scene, () => v.invalidate(), { frames, fixedFrame: "map" });
          const driveArm = publishUrdf(local, parseUrdf(ARM_URDF), { prefix: "arm/", markersId: "arm", framesId: "arm-tf" });
          frames.set("arm/base_link", "base_link", { t: { x: 0, y: 0, z: 0.3 }, q: { x: 0, y: 0, z: 0, w: 1 } });
          // relayed 3D layers (server, ROS 2) join the same scene and frame tree
          layers.add(new SceneLayerSink(c.scene, () => v.invalidate(), { frames, fixedFrame: "map" }));
          // ---- frame counter: wrap the host's render to publish frames per second once a second ----
          const n = points, pos = new Float32Array(n * 3), inten = new Float32Array(n);
          let frameCount = 0, fpsT = performance.now();
          const render = v.render.bind(v);
          v.render = () => { render(); frameCount++; const now = performance.now(); if (now - fpsT >= 1000) { setFps(Math.round(frameCount * 1000 / (now - fpsT))); frameCount = 0; fpsT = now; } };
          // ---- animation: every 50 ms move the robot, extend the trail, swing the arm; regenerate the cloud every `rate` ms ----
          const t0 = performance.now();
          let lastCloud = -Infinity;
          const timer = setInterval(() => {
            const t = (performance.now() - t0) / 1000;
            // robot drives a circle of radius 3
            const a = t * 0.4, x = 3 * Math.cos(a), y = 3 * Math.sin(a), yaw = a + Math.PI / 2;
            frames.set("base_link", "map", { t: { x, y, z: 0 }, q: quatFromEuler(0, 0, yaw) }, t);
            trail.append(x, y, 0.02);
            pose.setPose({ t: { x: 0, y: 0, z: 0 }, q: { x: 0, y: 0, z: 0, w: 1 } }, t);
            driveArm({ shoulder: 0.6 * Math.sin(t * 0.8) - 0.3, elbow: 1.2 + 0.8 * Math.sin(t * 1.1) }, t);
            if (t - lastCloud < rate / 1000) return;
            lastCloud = t;
            // spinning lidar: a ring with a wall on one side, intensity by range
            const rings = Math.max(1, Math.round(n / 720));
            for (let i = 0; i < n; i++) {
              const ring = i % rings, ang = (Math.floor(i / rings) / (n / rings)) * Math.PI * 2 + t;
              const r = 2 + 0.6 * Math.sin(ang * 3 + t) + (Math.cos(ang) > 0.8 ? -1.2 : 0) + (ring % 40) * 0.01;
              pos[3 * i] = r * Math.cos(ang); pos[3 * i + 1] = r * Math.sin(ang); pos[3 * i + 2] = 0.1 * Math.sin(ang * 5 + t * 2) + (ring / rings) * 1.5;
              inten[i] = r;
            }
            cloud.setPoints(pos, inten, null, t);
          }, 50);
          // stop the timer when the React host disposes the view (theme flip remounts it)
          const dispose = v.dispose.bind(v);
          v.dispose = () => { clearInterval(timer); dispose(); };
        }} />
      </div>
    </div>
  );
}
