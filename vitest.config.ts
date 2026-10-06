import { defineConfig } from "vitest/config";
export default defineConfig({
  test: {
    projects: [
      { test: { name: "core", include: ["ts/packages/core/src/**/*.test.ts"], environment: "node" } },
      { test: { name: "render", include: ["ts/packages/render/src/**/*.test.ts"], environment: "jsdom" } },
      { test: { name: "sources", include: ["ts/packages/sources/src/**/*.test.ts"], environment: "node" } },
      { test: { name: "source-template", include: ["ts/packages/source-template/src/**/*.test.ts"], environment: "node" } },
      { test: { name: "react", include: ["ts/packages/react/src/**/*.test.{ts,tsx}"], environment: "jsdom" } },
    ],
  },
});
