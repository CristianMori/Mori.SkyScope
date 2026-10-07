// Mori.SkyScope — Vite configuration of the plain TypeScript sample: the packages resolve to their sources so edits show up live.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { defineConfig } from "vite";
import path from "node:path";
export default defineConfig({
  resolve: {
    alias: {
      "@cmori/skyscope-core": path.resolve(__dirname, "../../ts/packages/core/src/index.ts"),
      "@cmori/skyscope-render": path.resolve(__dirname, "../../ts/packages/render/src/index.ts"),
      "@cmori/skyscope-sources": path.resolve(__dirname, "../../ts/packages/sources/src/index.ts"),
      "@cmori/skyscope-tokens/tokens.css": path.resolve(__dirname, "../../ts/packages/tokens/src/tokens.css"),
      "@cmori/skyscope-tokens": path.resolve(__dirname, "../../ts/packages/tokens/src/index.ts"),
    },
  },
});
