// Mori.SkyScope — Button component of the chrome around instruments.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { forwardRef, type ButtonHTMLAttributes } from "react";
import type { ButtonVariant, Size } from "@mori/skyscope-core";
import { cx } from "./cx.js";

/** Props of `Button`; every native button attribute is forwarded, `type` defaults to "button". */
export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  /** Visual variant from the design system (default "default"); maps to `.skyscope-button--<variant>`. */
  variant?: ButtonVariant | undefined;
  /** Control size from the design system (default "md"); maps to `.skyscope-button--<size>`. */
  size?: Size | undefined;
  /** Shows the spinner and disables the button (also sets `aria-busy`). */
  loading?: boolean | undefined;
  /** Stretches the button to its container's width. */
  block?: boolean | undefined;
  /** Square icon-only padding. */
  icon?: boolean | undefined;
  /** Highlighted (e.g. the active tool). */
  active?: boolean | undefined;
}

/** Chrome button, styled by the shared stylesheet (`.skyscope-button`). */
export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { variant = "default", size = "md", loading = false, block, icon, active, className, disabled, type = "button", children, ...rest }, ref) {
  return (
    <button ref={ref} type={type} disabled={disabled || loading} aria-busy={loading || undefined} aria-pressed={active}
      className={cx("skyscope-button", variant !== "default" && `skyscope-button--${variant}`, size !== "md" && `skyscope-button--${size}`,
        block && "skyscope-button--block", icon && "skyscope-button--icon", active && "skyscope-button--primary", className)} {...rest}>
      {loading && <span className="skyscope-button__spinner" aria-hidden="true" />}
      {children}
    </button>
  );
});
