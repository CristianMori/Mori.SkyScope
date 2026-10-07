// Mori.SkyScope — Entry point of the React sample.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "@cmori/skyscope-react/styles.css";
import { App } from "./App";

// Mount the dashboard into the #root element of index.html; StrictMode double-invokes effects in development, which the hosts tolerate.
createRoot(document.getElementById("root")!).render(<StrictMode><App /></StrictMode>);
