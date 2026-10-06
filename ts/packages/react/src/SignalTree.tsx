// Mori.SkyScope — React component hosting the signal tree panel beside a trend chart.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { useEffect, useRef, type CSSProperties } from "react";
import type { SignalStore } from "@mori/skyscope-core";
import { SignalTreePanel, type TrendChartView } from "@mori/skyscope-render";
import { cx } from "./cx.js";

/** Props of `SignalTree`. */
export interface SignalTreeProps {
  /** The store whose channels are listed; changing it remounts the panel. */
  store: SignalStore;
  /** The TrendChart rows are dragged onto (its view from `onReady`). */
  chart?: TrendChartView | null | undefined;
  /** Characters that split channel names into groups; changing it remounts the panel. */
  separators?: string | undefined;
  /** Placeholder text of the search box; changing it remounts the panel. */
  placeholder?: string | undefined;
  /** Class of the host div. */
  className?: string | undefined;
  /** Inline style of the host div; it takes the parent's full height. */
  style?: CSSProperties | undefined;
}

/** Searchable signal tree beside a TrendChart: drag channels (Ctrl/Shift for several) onto axes, lanes or the time axis. */
export function SignalTree({ store, chart, separators, placeholder, className, style }: SignalTreeProps) {
  const host = useRef<HTMLDivElement>(null);
  const panel = useRef<SignalTreePanel | null>(null);
  useEffect(() => {
    const p = new SignalTreePanel(host.current!, store, { chart, separators, placeholder });
    panel.current = p;
    return () => { p.dispose(); panel.current = null; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [store, separators, placeholder]);
  useEffect(() => { panel.current?.attach(chart ?? null); }, [chart]);
  return <div ref={host} className={cx("skyscope-tree-host", className)} style={{ height: "100%", minHeight: 0, ...style }} />;
}
