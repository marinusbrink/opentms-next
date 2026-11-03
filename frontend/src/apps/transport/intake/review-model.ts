import {
  fieldValueText,
  valueAtPath,
  withValueAtPath,
  type OrderIntakeFieldResolution,
  type OrderIntakePayload,
} from "@/domains/orders/order-intakes";
import { numberError, parseNumber } from "@/lib/forms/use-form-state";

/** The tenant feature of order intake (design 72 "Features"); the Intake view and the review need it on. */
export const ORDER_INTAKE_FEATURE = "Integrations.OrderIntake";

/** The channel of an intake ("Gmail", "Microsoft365", "Api") as its localized name; an unknown one as it is. */
export function channelLabel(t: (key: string) => string, channel: string | null | undefined): string {
  if (!channel) return "";
  const key = `Integrations:InboundChannel.${channel}`;
  const text = t(key);
  return text === key ? channel : text;
}

/* The review page's model (design 72 "Review"): the received values the clerk may correct, as texts by field path, and
 * the picks by field path. Pure, so the page's rules are testable without rendering. */

export type ReceivedKind = "text" | "number" | "date" | "time" | "multiline";

export interface ReceivedField {
  /** The field's name in the canonical model (`customerReference`, `weightKg`, `address.postalCode`). */
  name: string;
  kind: ReceivedKind;
  /** Spans the whole row of the section's grid. */
  wide?: boolean;
}

export const ORDER_FIELDS: ReceivedField[] = [
  { name: "customerReference", kind: "text" },
  { name: "orderDate", kind: "date" },
  { name: "orderTypeName", kind: "text" },
  { name: "customer.name", kind: "text" },
  { name: "customer.code", kind: "text" },
  { name: "customer.emailAddress", kind: "text" },
  { name: "customer.vatNumber", kind: "text" },
  { name: "customer.chamberOfCommerceNumber", kind: "text" },
  { name: "remarks", kind: "multiline", wide: true },
];

export const LINE_FIELDS: ReceivedField[] = [
  { name: "transportUnitName", kind: "text" },
  { name: "quantity", kind: "number" },
  { name: "colli", kind: "number" },
  { name: "positions", kind: "number" },
  { name: "weightKg", kind: "number" },
  { name: "volumeM3", kind: "number" },
  { name: "loadingMeters", kind: "number" },
  { name: "lengthCm", kind: "number" },
  { name: "widthCm", kind: "number" },
  { name: "heightCm", kind: "number" },
  { name: "productName", kind: "text" },
  { name: "temperatureName", kind: "text" },
  { name: "containerNumber", kind: "text" },
  { name: "barcode", kind: "text" },
  { name: "description", kind: "multiline", wide: true },
  { name: "remarks", kind: "multiline", wide: true },
];

export const STOP_FIELDS: ReceivedField[] = [
  { name: "waypointTypeName", kind: "text" },
  { name: "date", kind: "date" },
  { name: "timeFrom", kind: "time" },
  { name: "timeTill", kind: "time" },
  { name: "address.name", kind: "text" },
  { name: "address.street", kind: "text" },
  { name: "address.houseNumber", kind: "text" },
  { name: "address.postalCode", kind: "text" },
  { name: "address.city", kind: "text" },
  { name: "address.countryCode", kind: "text" },
  { name: "reference", kind: "text" },
  { name: "contactName", kind: "text" },
  { name: "contactPhone", kind: "text" },
  { name: "address.fullText", kind: "multiline", wide: true },
  { name: "remarks", kind: "multiline", wide: true },
];

/** `order.customerReference`, `lines[0].weightKg`, `lines[0].stops[1].address.city`. */
export const orderPath = (name: string) => `order.${name}`;
export const linePath = (line: number, name: string) => `lines[${line}].${name}`;
export const stopPath = (line: number, stop: number, name: string) => `lines[${line}].stops[${stop}].${name}`;

/** The text of a received value: the clerk's correction when there is one, else what was received. */
export function receivedText(base: OrderIntakePayload | null | undefined, overrides: Readonly<Record<string, string>>, path: string): string {
  return path in overrides ? overrides[path] : fieldValueText(valueAtPath(base, path));
}

/** The corrections that differ from what was received (an edit typed back to the original is no correction). */
export function effectiveOverrides(base: OrderIntakePayload | null | undefined, overrides: Readonly<Record<string, string>>): Record<string, string> {
  return Object.fromEntries(Object.entries(overrides).filter(([path, text]) => text.trim() !== fieldValueText(valueAtPath(base, path)).trim()));
}

/** The kind of the field a path ends in (numbers are parsed, the rest stays text). */
export function kindOfPath(path: string): ReceivedKind {
  const name = path.replace(/^order\./, "").replace(/^lines\[\d+\]\.(stops\[\d+\]\.)?/, "");
  const fields = path.includes(".stops[") ? STOP_FIELDS : path.startsWith("lines[") ? LINE_FIELDS : ORDER_FIELDS;
  return fields.find((field) => field.name === name)?.kind ?? "text";
}

/** The validation key per corrected path: numbers must parse and not be negative. */
export function overrideErrors(overrides: Readonly<Record<string, string>>): Record<string, string> {
  const errors: Record<string, string> = {};
  for (const [path, text] of Object.entries(overrides)) {
    if (kindOfPath(path) !== "number") continue;
    const error = numberError(text, { min: 0 });
    if (error) errors[path] = error;
  }
  return errors;
}

/** The reviewed payload: what was received with the corrections applied (numbers parsed, empty texts null); null without one. */
export function reviewedPayload(base: OrderIntakePayload | null | undefined, overrides: Readonly<Record<string, string>>): OrderIntakePayload | null {
  const changes = effectiveOverrides(base, overrides);
  if (Object.keys(changes).length === 0) return null;
  let next: OrderIntakePayload = structuredClone(base ?? {});
  for (const [path, text] of Object.entries(changes)) {
    const trimmed = text.trim();
    const value = kindOfPath(path) === "number" ? parseNumber(trimmed) : trimmed === "" ? null : trimmed;
    next = withValueAtPath(next, path, value);
  }
  return next;
}

/** The resolution entries by field path. */
export function resolutionByPath(resolution: readonly OrderIntakeFieldResolution[] | null | undefined): Map<string, OrderIntakeFieldResolution> {
  return new Map((resolution ?? []).filter((r) => r.fieldPath).map((r) => [r.fieldPath!, r]));
}

/** The text the document gave for an address: its full text, else its parts on one line. */
export function receivedAddressText(address: { name?: string | null; street?: string | null; houseNumber?: string | null; postalCode?: string | null; city?: string | null; countryCode?: string | null; fullText?: string | null } | null | undefined): string {
  if (!address) return "";
  if (address.fullText) return address.fullText;
  const street = [address.street, address.houseNumber].filter(Boolean).join(" ");
  const place = [address.postalCode, address.city].filter(Boolean).join(" ");
  return [address.name, street, place, address.countryCode].filter(Boolean).join(", ");
}

/** The pseudo-field of a refused order creation (design 72 build note 20): Unresolved, carrying the business error code. */
export const ORDER_REFUSAL_PATH = "order";

/** The refusal's text: the error code's own localized text (any module's resource), else a generic one. */
export function orderRefusalText(t: (key: string) => string, errorCode: string | null | undefined): string {
  const text = errorCode ? t(errorCode) : "";
  return text && text !== errorCode ? text : t("Orders:IntakeOrderRefusedUnknown");
}
