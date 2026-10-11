import type { LocalizedNameMap } from "@/domains/master-data/shared";

/* The editor side of Platform's LocalizedName (design 41): one text per configured language, the
 * default culture required. Mirrors LocalizedName.DefaultCulture and the launch languages. */
export const DEFAULT_CULTURE = "en";
export const CONFIGURED_CULTURES = ["nl", "en"] as const;

export type LocalizedNameValues = Record<(typeof CONFIGURED_CULTURES)[number], string>;

export function emptyLocalizedName(): LocalizedNameValues {
  return { nl: "", en: "" };
}

export function localizedNameFromMap(map: LocalizedNameMap | null | undefined): LocalizedNameValues {
  const values = emptyLocalizedName();
  for (const culture of CONFIGURED_CULTURES) {
    values[culture] = map?.[culture] ?? "";
  }
  return values;
}

/** The map the API accepts: empty texts dropped, so the server sees only real translations. */
export function localizedNameToMap(values: LocalizedNameValues): Record<string, string> {
  const map: Record<string, string> = {};
  for (const culture of CONFIGURED_CULTURES) {
    const text = values[culture].trim();
    if (text) map[culture] = text;
  }
  return map;
}

/** The validation error key for the default-culture field, or undefined when it is filled. */
export function validateLocalizedName(values: LocalizedNameValues): string | undefined {
  return values[DEFAULT_CULTURE].trim() ? undefined : "MasterData:Required";
}

/** Resolves a map the way the server does: requested culture, its language, the default, the first text. */
export function resolveLocalizedName(map: LocalizedNameMap | null | undefined, culture: string): string {
  if (!map) return "";
  const key = culture.toLowerCase();
  const language = key.split("-")[0];
  return map[key] ?? map[language] ?? map[DEFAULT_CULTURE] ?? Object.values(map).find((v) => v) ?? "";
}

/**
 * A single Name field for an international platform (PO, 2026-10-11): the editor shows the text of the
 * user's language and writes it back there; the other languages' texts are kept untouched. When the
 * default culture has no text yet, it gets the same text, so the server's required default is met and
 * every user sees a name.
 */
export function withTextForCulture(map: LocalizedNameMap | null | undefined, culture: string, text: string): Record<string, string> {
  const result: Record<string, string> = {};
  for (const [key, value] of Object.entries(map ?? {})) {
    if (value) result[key] = value;
  }
  const language = culture.toLowerCase().split("-")[0];
  const trimmed = text.trim();
  if (trimmed) {
    result[language] = trimmed;
    if (!result[DEFAULT_CULTURE]) result[DEFAULT_CULTURE] = trimmed;
  } else {
    delete result[language];
  }
  return result;
}
