import type { ReferenceOption } from "@/components/ui/reference-combobox";

/** The browser's IANA zones (every id the server accepts is in this list on a current runtime). Listing
 * zone ids is not output formatting (UI-FMT-001); the country editor and the address dialog share it. */
export function timeZoneOptions(): ReferenceOption[] {
  const ids = typeof Intl.supportedValuesOf === "function" ? Intl.supportedValuesOf("timeZone") : [];
  return ids.map((id) => ({ value: id, label: id }));
}
