/* The tenant's brand colour (tokens.md §1, D-01). Only this file sets --brand; every
 * other brand shade derives from it in opentms-theme.css. The theme comes from the
 * tenant settings App.UiManagement.Theme / CustomColor when the backend exposes them;
 * until then every tenant gets the product blue. */

export const BRAND_FALLBACK = "#0F6CBD";

const THEME_MAP: Record<string, string> = {
  default: "#FBA91A",
  red: "#E20613",
  green: "#006847",
  blue: "#38A8C6",
};

const HEX_COLOUR = /^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/;

export type TenantUiSettings = Record<string, string | null | undefined> | undefined | null;

export function resolveBrand(settings: TenantUiSettings): string {
  const theme = settings?.["App.UiManagement.Theme"]?.trim().toLowerCase();
  if (!theme) return BRAND_FALLBACK;
  if (theme === "custom") {
    const custom = settings?.["App.UiManagement.CustomColor"]?.trim() ?? "";
    return HEX_COLOUR.test(custom) ? custom : BRAND_FALLBACK;
  }
  return THEME_MAP[theme] ?? BRAND_FALLBACK;
}

export function applyTenantTheme(settings: TenantUiSettings): void {
  document.documentElement.style.setProperty("--brand", resolveBrand(settings));
}
