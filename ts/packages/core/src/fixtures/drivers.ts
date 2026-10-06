// Mori.SkyScope — Adapts one core module to the generic fixture format in spec/fixtures/*.json.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { createField, reduceField, fieldSnapshot, type FieldState, type FieldAction } from "../forms/field.js";
import type { ValidationRule } from "../forms/validation.js";
import { signalBufferDriver, decimationDriver } from "./signal-drivers.js";
import { scaleDriver } from "./scale-driver.js";
import { signalStoreDriver, synthDriver } from "./source-drivers.js";
import { paintDriver } from "./paint-driver.js";
import { geometryDriver, cameraDriver } from "./geometry-drivers.js";
import { interactionDriver, sceneDriver } from "./scene-drivers.js";
import { clipDriver } from "./clip-driver.js";
import { trendDriver } from "./trend-driver.js";
import { signalTreeDriver } from "./signal-tree-driver.js";
import { gaugeDriver } from "./gauge-driver.js";
import { cartesianDriver, pieDriver, polarDriver, heatmapDriver } from "./analytic-driver.js";
import { robotLayersDriver, sceneControllerDriver } from "./robot-drivers.js";
import { playbackDriver, mqttMappingDriver } from "./playback-drivers.js";
import { mcapDriver } from "./mcap-driver.js";
import { math3Driver, camera3dDriver, frameTreeDriver, layerMessageDriver, scene3dDriver, scene3dControllerDriver, meshFormatsDriver, urdfDriver } from "./scene3d-drivers.js";

/**
 * Adapts one core module to the generic fixture format in `spec/fixtures/*.json`.
 * The C# side has the identical interface in `Mori.SkyScope.Core.Tests/Fixtures/FixtureRunner.cs`.
 */
export interface FixtureDriver<S = unknown> {
  /** Fixture component name, matching the `component` field of the JSON file. */
  component: string;
  /** Build the initial state from a case's `setup` object. */
  create(setup: Record<string, unknown>): S;
  /** Apply one entry of a case's `steps`; may mutate and return the same state. */
  step(state: S, step: Record<string, unknown>): S;
  /** Everything a fixture may assert on. */
  snapshot(state: S): Record<string, unknown>;
}

interface FieldDriverState { field: FieldState; rules: ValidationRule[] }

/**
 * Form field reducer: `setup` gives the initial value and validation rules, every step is a `FieldAction`, the
 * snapshot is `fieldSnapshot`.
 */
export const fieldDriver: FixtureDriver<FieldDriverState> = {
  component: "field",
  create(setup) {
    const value = typeof setup.value === "string" ? setup.value : "";
    const rules = Array.isArray(setup.rules) ? (setup.rules as ValidationRule[]) : [];
    return { field: createField(value, rules), rules };
  },
  step(state, step) {
    return { ...state, field: reduceField(state.field, step as unknown as FieldAction, state.rules) };
  },
  snapshot(state) {
    return fieldSnapshot(state.field);
  },
};

/** Every driver by component name; `fixtures.test.ts` looks fixtures up here. */
export const fixtureDrivers: Record<string, FixtureDriver> = Object.fromEntries(
  [fieldDriver, signalBufferDriver, decimationDriver, scaleDriver, signalStoreDriver, synthDriver, paintDriver, geometryDriver, cameraDriver, interactionDriver, sceneDriver, clipDriver, trendDriver, signalTreeDriver, gaugeDriver, cartesianDriver, pieDriver, polarDriver, heatmapDriver, robotLayersDriver, sceneControllerDriver, playbackDriver, mqttMappingDriver, mcapDriver, math3Driver, camera3dDriver, frameTreeDriver, layerMessageDriver, scene3dDriver, scene3dControllerDriver, meshFormatsDriver, urdfDriver].map((d) => [d.component, d as FixtureDriver]),
);
