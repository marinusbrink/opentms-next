import { themeQuartz, type Theme } from "ag-grid-community";

/* The one AG Grid theme (UI-TOK-004): the Theming API fed from the tokens in
 * opentms-theme.css. Colours stay CSS variables so a runtime brand change applies;
 * the sizes are read once, when the first grid mounts. Never import ag-theme-*.css. */

function cssNumber(name: string, fallback: number): number {
  if (typeof document === "undefined") return fallback;
  const raw = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  const value = parseInt(raw, 10);
  return Number.isFinite(value) ? value : fallback;
}

let theme: Theme | undefined;

/** The Theming API params, read from the tokens (tokens.md §11). */
export function gridThemeParams() {
  return {
    fontFamily: "inherit",
    fontSize: cssNumber("--grid-font-size", 13),
    rowHeight: cssNumber("--grid-row-height", 36),
    headerHeight: cssNumber("--grid-header-height", 40),
    accentColor: "var(--brand)",
    selectedRowBackgroundColor: "var(--grid-selected-row-bg)",
    rowHoverColor: "var(--brand-subtle)",
    borderColor: "var(--border)",
    headerBackgroundColor: "var(--surface-page)",
    foregroundColor: "var(--foreground)",
    backgroundColor: "var(--background)",
    wrapperBorderRadius: 0,
    cellHorizontalPadding: 12,
  } as const;
}

export function opentmsGridTheme(): Theme {
  if (!theme) {
    theme = themeQuartz.withParams(gridThemeParams());
  }
  return theme;
}

/** Test seam: forget the cached theme so token changes are picked up. */
export function resetGridThemeForTests(): void {
  theme = undefined;
}
