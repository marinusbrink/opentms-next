import type { RelationOption } from "@/domains/master-data/entities";
import { parseTimeOfDay, timeOfDay } from "@/domains/orders/reference-data";
import type { TemplateDto, TemplateLineDto, TemplateLineInputDto, TemplateStopDto, TemplateStopInputDto, TemplateTripRequestInputDto } from "@/domains/orders/templates";
import type {
  PaymentInstruction,
  TransportOrderDefaultStopDto,
  TransportOrderDefaultStopInputDto,
  TransportOrderDto,
  TransportOrderFinancialTermsDto,
  TransportOrderFinancialTermsInputDto,
  TransportOrderLineDto,
  TransportOrderLineInputDto,
  TransportOrderMeasurementDto,
  TransportOrderStopDto,
  TransportOrderStopInputDto,
  TransportOrderTripRequestDto,
  TransportOrderTripRequestInputDto,
  PricePreviewRequestDto,
} from "@/domains/orders/transport-orders";
import type { MultipleMatches } from "@/domains/financial/rate-lines";
import { relationOptionOf } from "@/apps/transport/components/RelationFields";
import { isSameValue, numberError, numberToText, parseInteger, parseNumber, type FieldErrors } from "@/lib/forms/use-form-state";

/* The form model of the order page (design 61 "Order page, legacy tab structure"), shared by the order page and the
 * template page ("templates use the same page in template mode"). Values are strings where the user types numbers; the DTO mappers
 * below parse on save. A stop carries a date (order) or a day offset (template); the mapper picks the right one. On
 * screen the API's "line" is a freight item; the code keeps the API's name. The order has no stop pattern any more
 * (PO 2026-10-26): `defaultStops` is carried unchanged so a save never drops what an older order stored. */

export type EditorMode = "order" | "template";

export interface StopValues {
  key: string;
  id: string | null;
  waypointTypeId: string | null;
  waypointTypeName: string;
  relationId: string | null;
  relationName: string;
  relationAddressId: string | null;
  /** The address's city, shown in the freight item list (from → to); not sent. */
  city: string;
  contactPersonId: string | null;
  /** Order lines: `YYYY-MM-DD`. */
  date: string | null;
  /** The pattern and template lines: days from the order date, as typed. */
  dayOffset: string;
  timeSlotId: string | null;
  /** `HH:mm`, as typed (UI-FMT-004). */
  windowFrom: string;
  windowTill: string;
  reference: string;
  remarks: string;
  plannedDurationMin: string;
}

export interface LineValues {
  key: string;
  id: string | null;
  transportUnitId: string | null;
  transportUnitName: string;
  productId: string | null;
  productName: string;
  productRelationCodeId: string | null;
  temperatureId: string | null;
  vehicleTypeId: string | null;
  quantity: string;
  positions: string;
  colli: string;
  weightKg: string;
  volumeM3: string;
  loadingMeters: string;
  loadFactor: string;
  epq: string;
  lengthCm: string;
  widthCm: string;
  heightCm: string;
  /** The individual weighings and volumes of the line (design 58: up to three per kind), as typed; position = index + 1. */
  weightMeasurements: string[];
  volumeMeasurements: string[];
  invoiceReference: string;
  remarks: string;
  internalRemarks: string;
  shipperInstructions: string;
  specialAgreements: string;
  attachedDocuments: string;
  paymentInstruction: PaymentInstruction;
  containerNumber: string;
  sizeOrIso: string;
  pinCode: string;
  shippingCompany: string;
  shipperRelation: RelationOption | null;
  barcode: string;
  requiredTransportCharacteristicIds: string[];
  attributes: Record<string, string>;
  stops: StopValues[];
  /** Stored derived values (order mode), shown read-only. */
  distanceM: number | null;
  durationS: number | null;
  co2Kg: number | null;
}

export interface ReferenceValues {
  key: string;
  type: string;
  value: string;
  description: string;
}

export interface TermsValues {
  currencyCode: string;
  vatPercentageId: string | null;
  paymentTermDays: string;
  priceListIds: string[];
  multipleMatches: MultipleMatches;
  discountsOrSurcharges: { discountOrSurchargeId: string; source: 1 | 2 }[];
  fixedPriceAmount: string;
  sendFreightDocumentsWithInvoice: boolean;
}

export interface TripRequestValues {
  enabled: boolean;
  transportTripTypeId: string | null;
  departureRelation: RelationOption | null;
  departureAddressId: string | null;
  arrivalRelation: RelationOption | null;
  arrivalAddressId: string | null;
  /** Order: `YYYY-MM-DD` and `HH:mm`; template: a day offset and `HH:mm`. */
  departureDate: string | null;
  departureDayOffset: string;
  departureTime: string;
  arrivalDate: string | null;
  arrivalDayOffset: string;
  arrivalTime: string;
  carrierRelation: RelationOption | null;
  carrierContactPersonId: string | null;
  vehicleCombinationId: string | null;
  vehicleIds: string[];
  driverIds: string[];
  agreedPurchaseAmount: string;
  agreedPurchaseCurrencyCode: string;
  /** The freight items whose legs do not go on the trip (PO 2026-10-26: a checkbox per item, default all); by the
   *  item's form key, so a new item is on the trip until it is unticked. */
  excludedLineKeys: string[];
}

export interface ScheduleValues {
  recurrence: 0 | 1 | 2;
  interval: string;
  daysOfWeek: number;
  dayOfMonth: string;
  horizonDays: string;
  validFrom: string | null;
  validTill: string | null;
}

export interface OrderValues {
  transportOrderTypeId: string | null;
  customer: RelationOption | null;
  customerContactPersonId: string | null;
  orderDate: string | null;
  customerReference: string;
  organizationUnitId: string | null;
  internalRemarks: string;
  invoicingRemarks: string;
  attributes: Record<string, string>;
  defaultStops: StopValues[];
  lines: LineValues[];
  references: ReferenceValues[];
  terms: TermsValues;
  tripRequest: TripRequestValues;
  /* Template mode */
  name: string;
  description: string;
  isActive: boolean;
  schedule: ScheduleValues;
}

let keyCounter = 0;
export const newKey = (prefix: string) => `${prefix}-${++keyCounter}`;

/* ── From the DTOs ───────────────────────────────────────────────────────────────────────────────────────────── */

/** MeasurementKind (design 58): WeightKg = 1, VolumeM3 = 2; at most three of each (OrdersConsts.MaxMeasurementsPerKind). */
export const MEASUREMENT_KIND = { WeightKg: 1, VolumeM3: 2 } as const;
export const MAX_MEASUREMENTS_PER_KIND = 3;

/** One kind's measurements as the three typed slots (position 1..3). */
export function measurementTexts(measurements: TransportOrderMeasurementDto[] | null | undefined, kind: number): string[] {
  const slots = Array.from({ length: MAX_MEASUREMENTS_PER_KIND }, () => "");
  for (const m of measurements ?? []) {
    const index = (m.position ?? 0) - 1;
    if (m.kind === kind && index >= 0 && index < MAX_MEASUREMENTS_PER_KIND) slots[index] = numberToText(m.value);
  }
  return slots;
}

/** The typed slots back as the line's measurements; an empty slot is none. */
export function measurementsOf(weights: string[], volumes: string[]): TransportOrderMeasurementDto[] {
  const of = (texts: string[], kind: 1 | 2) =>
    texts.flatMap((text, index) => {
      const value = parseNumber(text);
      return value == null ? [] : [{ kind, position: index + 1, value }];
    });
  return [...of(weights, MEASUREMENT_KIND.WeightKg), ...of(volumes, MEASUREMENT_KIND.VolumeM3)];
}

export function stopValuesOf(stop: TransportOrderStopDto | TransportOrderDefaultStopDto | TemplateStopDto): StopValues {
  const s = stop as Partial<TransportOrderStopDto & TransportOrderDefaultStopDto & TemplateStopDto>;
  return {
    key: newKey("stop"),
    id: ("id" in s ? s.id : null) ?? null,
    waypointTypeId: s.waypointTypeId ?? null,
    waypointTypeName: s.waypointTypeName ?? "",
    relationId: s.relationId ?? null,
    relationName: s.relationName ?? "",
    relationAddressId: s.relationAddressId ?? null,
    city: (s as Partial<TransportOrderStopDto>).city ?? "",
    contactPersonId: s.contactPersonId ?? null,
    date: s.date ?? null,
    dayOffset: numberToText(s.dayOffset ?? 0),
    timeSlotId: s.timeSlotId ?? null,
    windowFrom: timeOfDay(s.windowFrom),
    windowTill: timeOfDay(s.windowTill),
    reference: s.reference ?? "",
    remarks: s.remarks ?? "",
    plannedDurationMin: numberToText(s.plannedDurationMin),
  };
}

export function newStop(orderDate: string | null, pattern?: StopValues): StopValues {
  return {
    ...(pattern ?? {
      waypointTypeId: null,
      waypointTypeName: "",
      relationId: null,
      relationName: "",
      relationAddressId: null,
      city: "",
      contactPersonId: null,
      timeSlotId: null,
      windowFrom: "",
      windowTill: "",
      reference: "",
      remarks: "",
      plannedDurationMin: "",
      dayOffset: "0",
    }),
    key: newKey("stop"),
    id: null,
    date: pattern ? addDays(orderDate, parseInteger(pattern.dayOffset) ?? 0) : orderDate,
  };
}

/** `YYYY-MM-DD` plus whole days, as a calendar date (no timezone shift). */
export function addDays(isoDate: string | null, days: number): string | null {
  if (!isoDate) return null;
  const [y, m, d] = isoDate.split("-").map(Number);
  const date = new Date(y, m - 1, d + days);
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}

export function lineValuesOf(line: TransportOrderLineDto | TemplateLineDto): LineValues {
  const l = line as Partial<TransportOrderLineDto & TemplateLineDto>;
  return {
    key: newKey("line"),
    id: l.id ?? null,
    transportUnitId: l.transportUnitId ?? null,
    transportUnitName: l.transportUnitName ?? "",
    productId: l.productId ?? null,
    productName: l.productName ?? "",
    productRelationCodeId: l.productRelationCodeId ?? null,
    temperatureId: l.temperatureId ?? null,
    vehicleTypeId: l.vehicleTypeId ?? null,
    quantity: numberToText(l.quantity),
    positions: numberToText(l.positions),
    colli: numberToText(l.colli),
    weightKg: numberToText(l.weightKg),
    volumeM3: numberToText(l.volumeM3),
    loadingMeters: numberToText(l.loadingMeters),
    loadFactor: numberToText(l.loadFactor),
    epq: numberToText(l.epq),
    lengthCm: numberToText(l.lengthCm),
    widthCm: numberToText(l.widthCm),
    heightCm: numberToText(l.heightCm),
    weightMeasurements: measurementTexts(l.measurements, MEASUREMENT_KIND.WeightKg),
    volumeMeasurements: measurementTexts(l.measurements, MEASUREMENT_KIND.VolumeM3),
    invoiceReference: l.invoiceReference ?? "",
    remarks: l.remarks ?? "",
    internalRemarks: l.internalRemarks ?? "",
    shipperInstructions: l.shipperInstructions ?? "",
    specialAgreements: l.specialAgreements ?? "",
    attachedDocuments: l.attachedDocuments ?? "",
    paymentInstruction: l.paymentInstruction ?? 0,
    containerNumber: l.containerNumber ?? "",
    sizeOrIso: l.sizeOrIso ?? "",
    pinCode: l.pinCode ?? "",
    shippingCompany: l.shippingCompany ?? "",
    shipperRelation: relationOptionOf(l.shipperRelationId, (l as TransportOrderLineDto).shipperRelationName),
    barcode: l.barcode ?? "",
    requiredTransportCharacteristicIds: l.requiredTransportCharacteristicIds ?? [],
    attributes: l.attributes ?? {},
    stops: (l.stops ?? []).map(stopValuesOf),
    distanceM: (l as TransportOrderLineDto).distanceM ?? null,
    durationS: (l as TransportOrderLineDto).durationS ?? null,
    co2Kg: (l as TransportOrderLineDto).co2Kg ?? null,
  };
}

/** One end of a pasted freight item (Paste from Excel): the relation, its default (or only) address and its city. */
export interface EntryEnd {
  relation: RelationOption | null;
  addressId: string | null;
  city: string;
}

/** A pasted row (design 61 "Order page, legacy tab structure" 3, Paste from Excel): from relation, to relation, date
 * (template: day offset) and time, transport unit, quantity, weight, reference. Extra stops, cargo and container are
 * edited in the item's Edit dialog. */
export interface ItemEntry {
  from: EntryEnd;
  to: EntryEnd;
  /** Order: `YYYY-MM-DD`. */
  date: string | null;
  /** Template: days from the order date, as typed. */
  dayOffset: string;
  /** `HH:mm` as typed: the start of the loading window. */
  time: string;
  transportUnitId: string | null;
  transportUnitName: string;
  quantity: string;
  weightKg: string;
  /** The loading (first) stop's reference. */
  reference: string;
}

export const EMPTY_END: EntryEnd = { relation: null, addressId: null, city: "" };

export const emptyEntry = (date: string | null): ItemEntry => ({ from: EMPTY_END, to: EMPTY_END, date, dayOffset: "0", time: "", transportUnitId: null, transportUnitName: "", quantity: "", weightKg: "", reference: "" });

/** The waypoint types a pasted item's first and last stop get: the order type's first and last default waypoint
 * (normally Load and Unload). */
export interface EntryWaypoints {
  from: { id: string; name: string } | null;
  to: { id: string; name: string } | null;
}

type DefaultWaypoint = { waypointTypeId?: string; name?: string | null; position?: number };
type WaypointTypeActivity = { id?: string; name?: string | null; activity?: number };

/** The waypoint types for a pasted item: the order type's first and last default waypoint; without those, the first
 * waypoint type whose activity loads and the first that unloads. */
export function entryWaypointsOf(defaults: readonly DefaultWaypoint[] | null | undefined, types: readonly WaypointTypeActivity[] | undefined): EntryWaypoints {
  const ordered = [...(defaults ?? [])].filter((w) => w.waypointTypeId).sort((a, b) => (a.position ?? 0) - (b.position ?? 0));
  const of = (w: { waypointTypeId?: string; name?: string | null } | undefined) => (w?.waypointTypeId ? { id: w.waypointTypeId, name: w.name ?? "" } : null);
  const byActivity = (activity: number) => {
    const type = (types ?? []).find((t) => t.activity === activity && t.id);
    return type ? { id: type.id!, name: type.name ?? "" } : null;
  };
  if (ordered.length > 0) return { from: of(ordered[0]), to: ordered.length > 1 ? of(ordered.at(-1)) : byActivity(2) };
  return { from: byActivity(1), to: byActivity(2) };
}

/** The freight item a pasted row adds: two stops, From with the date, time and reference, To on the same date; the
 * quantity defaults to 1. */
export function itemFromEntry(entry: ItemEntry, waypoints: EntryWaypoints): LineValues {
  const stop = (end: EntryEnd, waypoint: EntryWaypoints["from"], first: boolean): StopValues => ({
    key: newKey("stop"),
    id: null,
    waypointTypeId: waypoint?.id ?? null,
    waypointTypeName: waypoint?.name ?? "",
    relationId: end.relation?.id ?? null,
    relationName: end.relation?.name ?? "",
    relationAddressId: end.addressId,
    city: end.city,
    contactPersonId: null,
    date: entry.date,
    dayOffset: entry.dayOffset.trim() || "0",
    timeSlotId: null,
    windowFrom: first ? (parseTimeOfDay(entry.time) ? timeOfDay(parseTimeOfDay(entry.time)) : "") : "",
    windowTill: "",
    reference: first ? entry.reference.trim() : "",
    remarks: "",
    plannedDurationMin: "",
  });
  return {
    ...newLine([stop(entry.from, waypoints.from, true), stop(entry.to, waypoints.to, false)]),
    transportUnitId: entry.transportUnitId,
    transportUnitName: entry.transportUnitName,
    quantity: entry.quantity.trim() || "1",
    weightKg: entry.weightKg.trim(),
  };
}

/* ── The stop strip and the freight fields of the item being entered (design 61 "Order page, legacy tab structure" 3) ── */

/** A freight item has at least two stops (the strip's − never goes below). */
export const MIN_STOPS = 2;
/** design 56: at most 50 stops per freight item. */
export const MAX_STOPS = 50;

/** The waypoint types the stop strip starts with: every default waypoint of the order type in its order (number and
 * activities); without any, the first waypoint type that loads and the first that unloads; never fewer than two rows
 * (a missing one starts without an activity). */
export function stripWaypointsOf(defaults: readonly DefaultWaypoint[] | null | undefined, types: readonly WaypointTypeActivity[] | undefined): ({ id: string; name: string } | null)[] {
  const ordered = [...(defaults ?? [])].filter((w) => w.waypointTypeId).sort((a, b) => (a.position ?? 0) - (b.position ?? 0));
  const list: ({ id: string; name: string } | null)[] =
    ordered.length > 0
      ? ordered.map((w) => ({ id: w.waypointTypeId!, name: w.name ?? "" }))
      : [1, 2].map((activity) => {
          const type = (types ?? []).find((t) => t.activity === activity && t.id);
          return type ? { id: type.id!, name: type.name ?? "" } : null;
        });
  while (list.length < MIN_STOPS) list.push(null);
  return list;
}

/** An empty stop of the strip: the given activity, the order date (template: day 0). */
export function stripStop(waypoint: { id: string; name: string } | null, date: string | null): StopValues {
  return { ...newStop(date), waypointTypeId: waypoint?.id ?? null, waypointTypeName: waypoint?.name ?? "" };
}

/** The strip's starting stops for the order type's waypoints. */
export const stripStopsOf = (waypoints: readonly ({ id: string; name: string } | null)[], date: string | null): StopValues[] => waypoints.map((w) => stripStop(w, date));

/** The freight fields of the item being entered, in one row next to Add. */
export interface FreightEntry {
  transportUnitId: string | null;
  transportUnitName: string;
  quantity: string;
  productId: string | null;
  productName: string;
  positions: string;
  colli: string;
  weightKg: string;
  loadingMeters: string;
  volumeM3: string;
}

export const emptyFreightEntry = (): FreightEntry => ({ transportUnitId: null, transportUnitName: "", quantity: "", productId: null, productName: "", positions: "", colli: "", weightKg: "", loadingMeters: "", volumeM3: "" });

/** After Add the freight fields clear except the transport unit (the next item is often the same unit). */
export const nextFreightEntry = (entry: FreightEntry): FreightEntry => ({ ...emptyFreightEntry(), transportUnitId: entry.transportUnitId, transportUnitName: entry.transportUnitName });

export type FreightEntryField = "transportUnitId" | "quantity" | "positions" | "colli" | "weightKg" | "loadingMeters" | "volumeM3";

/** What blocks Add in the freight fields (UI-FORM-006 on Enter): the transport unit (a save needs it) and the numbers'
 * shape. The order type's required flags are marks only, as on the order (enforced on Confirm). */
export function freightEntryErrors(entry: FreightEntry): Partial<Record<FreightEntryField, string>> {
  const errors: Partial<Record<FreightEntryField, string>> = {};
  if (!entry.transportUnitId) errors.transportUnitId = "MasterData:Required";
  for (const field of ["quantity", "positions", "colli", "weightKg", "loadingMeters", "volumeM3"] as const) if (numberError(entry[field], { min: 0 })) errors[field] = "MasterData:InvalidNumber";
  return errors;
}

/** The freight item Add makes: the freight fields and a copy of the strip's stops (new keys, no ids); the quantity
 * defaults to 1. The strip keeps its own stops for the next item. */
export function itemFromStrip(stops: readonly StopValues[], entry: FreightEntry): LineValues {
  return {
    ...newLine(stops.map((stop) => ({ ...stop, key: newKey("stop"), id: null }))),
    transportUnitId: entry.transportUnitId,
    transportUnitName: entry.transportUnitName,
    productId: entry.productId,
    productName: entry.productName,
    quantity: entry.quantity.trim() || "1",
    positions: entry.positions.trim(),
    colli: entry.colli.trim(),
    weightKg: entry.weightKg.trim(),
    loadingMeters: entry.loadingMeters.trim(),
    volumeM3: entry.volumeM3.trim(),
  };
}

/* ── What is wrong with a stop: the message names the stop ("Stop 2 (Unload): …", design 61 legacy tab structure 9) ── */

export type StopField = "waypointTypeId" | "relationId" | "relationAddressId" | "date" | "window";

export interface StopProblem {
  /** 0-based position in the item's stops. */
  index: number;
  field: StopField;
  /** The reason's localization key, without the stop's name. */
  key: string;
}

/** Every problem of the stops, in stop order: an activity, a relation, a usable address (one of the relation's,
 * chosen), the date (order mode: the API's stop date is required) and the window's time shape. */
export function stopProblemsOf(stops: readonly StopValues[], mode: EditorMode): StopProblem[] {
  const problems: StopProblem[] = [];
  stops.forEach((stop, index) => {
    if (!stop.waypointTypeId) problems.push({ index, field: "waypointTypeId", key: "Orders:StopNeedsActivity" });
    if (!stop.relationId) problems.push({ index, field: "relationId", key: "Orders:StopNeedsRelation" });
    else if (!stop.relationAddressId) problems.push({ index, field: "relationAddressId", key: "Orders:StopNeedsAddress" });
    if (mode === "order" && !stop.date) problems.push({ index, field: "date", key: "Orders:StopNeedsDate" });
    if ((stop.windowFrom.trim() && !parseTimeOfDay(stop.windowFrom)) || (stop.windowTill.trim() && !parseTimeOfDay(stop.windowTill))) problems.push({ index, field: "window", key: "Orders:InvalidTime" });
  });
  return problems;
}

/** "Stop 2 (Unload)", or "Stop 2" while the stop has no activity. */
export function stopName(t: (key: string) => string, stop: Pick<StopValues, "waypointTypeName"> | undefined, index: number): string {
  const name = stop?.waypointTypeName.trim();
  return (name ? t("Orders:StopNamed").replace("{1}", name) : t("Orders:StopNumbered")).replace("{0}", String(index + 1));
}

/** "Stop 2 (Unload): choose an address of the relation." */
export function stopProblemText(t: (key: string) => string, stops: readonly Pick<StopValues, "waypointTypeName">[], problem: StopProblem): string {
  return `${stopName(t, stops[problem.index], problem.index)}: ${t(problem.key)}`;
}

/** An empty freight item with the given stops (the stop strip, `itemFromStrip`, or a pasted row, `itemFromEntry`, builds them). */
export function newLine(stops: StopValues[] = []): LineValues {
  return {
    key: newKey("line"),
    id: null,
    transportUnitId: null,
    transportUnitName: "",
    productId: null,
    productName: "",
    productRelationCodeId: null,
    temperatureId: null,
    vehicleTypeId: null,
    quantity: "1",
    positions: "",
    colli: "",
    weightKg: "",
    volumeM3: "",
    loadingMeters: "",
    loadFactor: "",
    epq: "",
    lengthCm: "",
    widthCm: "",
    heightCm: "",
    weightMeasurements: ["", "", ""],
    volumeMeasurements: ["", "", ""],
    invoiceReference: "",
    remarks: "",
    internalRemarks: "",
    shipperInstructions: "",
    specialAgreements: "",
    attachedDocuments: "",
    paymentInstruction: 0,
    containerNumber: "",
    sizeOrIso: "",
    pinCode: "",
    shippingCompany: "",
    shipperRelation: null,
    barcode: "",
    requiredTransportCharacteristicIds: [],
    attributes: {},
    stops,
    distanceM: null,
    durationS: null,
    co2Kg: null,
  };
}

export const DEFAULT_TERMS: TermsValues = {
  currencyCode: "EUR",
  vatPercentageId: null,
  paymentTermDays: "",
  priceListIds: [],
  multipleMatches: 1,
  discountsOrSurcharges: [],
  fixedPriceAmount: "",
  sendFreightDocumentsWithInvoice: false,
};

export function termsValuesOf(terms: TransportOrderFinancialTermsDto | undefined): TermsValues {
  if (!terms) return DEFAULT_TERMS;
  return {
    currencyCode: terms.currencyCode ?? "EUR",
    vatPercentageId: terms.vatPercentageId ?? null,
    paymentTermDays: numberToText(terms.paymentTermDays),
    priceListIds: terms.priceListIds ?? [],
    multipleMatches: terms.multipleMatches ?? 1,
    discountsOrSurcharges: (terms.discountsOrSurcharges ?? []).map((d) => ({ discountOrSurchargeId: d.discountOrSurchargeId, source: (d.source ?? 2) as 1 | 2 })),
    fixedPriceAmount: numberToText(terms.fixedPriceAmount),
    sendFreightDocumentsWithInvoice: terms.sendFreightDocumentsWithInvoice ?? false,
  };
}

/** Terms as an API input carries them (the customer's defaults, NewOrderDialog.termsFromDefaults) as form values;
 *  none means the module's default, as the server starts an order without the customer's settings (design 58). */
export function termsValuesOfInput(input: TransportOrderFinancialTermsInputDto | undefined): TermsValues {
  if (!input) return DEFAULT_TERMS;
  return {
    currencyCode: input.currencyCode ?? "EUR",
    vatPercentageId: input.vatPercentageId ?? null,
    paymentTermDays: numberToText(input.paymentTermDays),
    priceListIds: input.priceListIds ?? [],
    multipleMatches: input.multipleMatches ?? 1,
    discountsOrSurcharges: (input.discountsOrSurcharges ?? []).map((d) => ({ discountOrSurchargeId: d.discountOrSurchargeId, source: (d.source ?? 1) as 1 | 2 })),
    fixedPriceAmount: numberToText(input.fixedPriceAmount),
    sendFreightDocumentsWithInvoice: input.sendFreightDocumentsWithInvoice ?? false,
  };
}

export const EMPTY_TRIP_REQUEST: TripRequestValues = {
  enabled: false,
  transportTripTypeId: null,
  departureRelation: null,
  departureAddressId: null,
  arrivalRelation: null,
  arrivalAddressId: null,
  departureDate: null,
  departureDayOffset: "0",
  departureTime: "",
  arrivalDate: null,
  arrivalDayOffset: "",
  arrivalTime: "",
  carrierRelation: null,
  carrierContactPersonId: null,
  vehicleCombinationId: null,
  vehicleIds: [],
  driverIds: [],
  agreedPurchaseAmount: "",
  agreedPurchaseCurrencyCode: "",
  excludedLineKeys: [],
};

/** An ISO instant → the local `YYYY-MM-DD` and `HH:mm` the fields show (UI-FMT-004); conversion through Date only. */
export function splitDateTime(value: string | null | undefined): { date: string | null; time: string } {
  if (!value) return { date: null, time: "" };
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return { date: null, time: "" };
  const pad = (n: number) => String(n).padStart(2, "0");
  return { date: `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`, time: `${pad(d.getHours())}:${pad(d.getMinutes())}` };
}

/** `YYYY-MM-DD` + `HH:mm` → an ISO instant in the browser's zone; null when the date is missing. */
export function joinDateTime(date: string | null, time: string): string | null {
  if (!date) return null;
  const [y, m, d] = date.split("-").map(Number);
  const parsed = parseTimeOfDay(time || "00:00");
  const [hh, mm] = (parsed ?? "00:00:00").split(":").map(Number);
  return new Date(y, m - 1, d, hh, mm).toISOString();
}

/** The Tevens trip's prefill when the user ticks "Create a trip" (design 61, easier than OpenTMS): only the empty fields
 * are filled, the user can change all of them. Trip type: the tenant's default (or only) type; departure: the first
 * stop of the first line, with its date and window start; arrival: the last stop of the last line. A template carries
 * day offsets instead of dates. */
export function tripRequestWithDefaults(request: TripRequestValues, allLines: LineValues[], mode: EditorMode, defaultTripTypeId: string | null): TripRequestValues {
  /* Only the freight items that go on the trip give its departure and arrival. */
  const lines = allLines.filter((line) => !request.excludedLineKeys.includes(line.key));
  const first = lines[0]?.stops[0];
  const last = lines.at(-1)?.stops.at(-1);
  const next = { ...request };
  if (!next.transportTripTypeId && defaultTripTypeId) next.transportTripTypeId = defaultTripTypeId;
  if (!next.departureRelation && first?.relationId) {
    next.departureRelation = relationOptionOf(first.relationId, first.relationName);
    next.departureAddressId = first.relationAddressId;
  }
  if (!next.arrivalRelation && last?.relationId) {
    next.arrivalRelation = relationOptionOf(last.relationId, last.relationName);
    next.arrivalAddressId = last.relationAddressId;
  }
  if (first) {
    if (mode === "order") {
      if (!next.departureDate && first.date) {
        next.departureDate = first.date;
        if (!next.departureTime.trim() && first.windowFrom.trim()) next.departureTime = first.windowFrom.trim();
      }
    } else if ((next.departureDayOffset.trim() === "" || next.departureDayOffset.trim() === "0") && !next.departureTime.trim()) {
      next.departureDayOffset = first.dayOffset.trim() || "0";
      if (first.windowFrom.trim()) next.departureTime = first.windowFrom.trim();
    }
  }
  return next;
}

/** The trip type the Tevens trip starts with: the tenant's default type when it is among the active ones, else the only one. */
export function defaultTripTypeIdOf(defaultId: string | null | undefined, activeIds: readonly string[]): string | null {
  if (defaultId && activeIds.includes(defaultId)) return defaultId;
  return activeIds.length === 1 ? activeIds[0] : null;
}

export function tripRequestValuesOf(request: TransportOrderTripRequestDto | TemplateTripRequestInputDto | undefined, lines: LineValues[] = []): TripRequestValues {
  if (!request) return EMPTY_TRIP_REQUEST;
  /* `lineIds` (design 61 "Order page, legacy tab structure" 8): null = every item; a stored `[]` (the last chosen item was removed)
   * means none: shown as nothing ticked, which the validation refuses until the user chooses (never posted back). */
  const lineIds = (request as { lineIds?: string[] | null }).lineIds?.map((id) => id.toLowerCase()) ?? null;
  const r = request as Partial<TransportOrderTripRequestDto & TemplateTripRequestInputDto>;
  const departure = splitDateTime(r.plannedDeparture);
  const arrival = splitDateTime(r.plannedArrival);
  return {
    enabled: true,
    transportTripTypeId: r.transportTripTypeId ?? null,
    departureRelation: relationOptionOf(r.departureRelationId, r.departureRelationName),
    departureAddressId: r.departureAddressId ?? null,
    arrivalRelation: relationOptionOf(r.arrivalRelationId, r.arrivalRelationName),
    arrivalAddressId: r.arrivalAddressId ?? null,
    departureDate: departure.date,
    departureDayOffset: numberToText(r.departureDayOffset ?? 0),
    departureTime: r.departureTime != null ? timeOfDay(r.departureTime) : departure.time,
    arrivalDate: arrival.date,
    arrivalDayOffset: numberToText(r.arrivalDayOffset),
    arrivalTime: r.arrivalTime != null ? timeOfDay(r.arrivalTime) : arrival.time,
    carrierRelation: relationOptionOf(r.carrierRelationId, r.carrierRelationName),
    carrierContactPersonId: r.carrierContactPersonId ?? null,
    vehicleCombinationId: r.vehicleCombinationId ?? null,
    vehicleIds: r.vehicleIds ?? [],
    driverIds: r.driverIds ?? [],
    agreedPurchaseAmount: numberToText(r.agreedPurchaseAmount),
    agreedPurchaseCurrencyCode: r.agreedPurchaseCurrencyCode ?? "",
    excludedLineKeys: lineIds ? lines.filter((line) => !line.id || !lineIds.includes(line.id.toLowerCase())).map((line) => line.key) : [],
  };
}

export const DEFAULT_SCHEDULE: ScheduleValues = { recurrence: 0, interval: "1", daysOfWeek: 0, dayOfMonth: "", horizonDays: "5", validFrom: null, validTill: null };

export function orderValuesOf(dto: TransportOrderDto | undefined): OrderValues {
  const lines = (dto?.lines ?? []).map(lineValuesOf);
  return {
    transportOrderTypeId: dto?.transportOrderTypeId ?? null,
    customer: relationOptionOf(dto?.customerRelationId, dto?.customerName),
    customerContactPersonId: dto?.customerContactPersonId ?? null,
    orderDate: dto?.orderDate ?? null,
    customerReference: dto?.customerReference ?? "",
    organizationUnitId: dto?.organizationUnitId ?? null,
    internalRemarks: dto?.internalRemarks ?? "",
    invoicingRemarks: dto?.invoicingRemarks ?? "",
    attributes: dto?.attributes ?? {},
    defaultStops: (dto?.defaultStops ?? []).map(stopValuesOf),
    lines,
    references: (dto?.references ?? []).map((r) => ({ key: newKey("ref"), type: r.type, value: r.value, description: r.description ?? "" })),
    terms: termsValuesOf(dto?.financialTerms),
    tripRequest: tripRequestValuesOf(dto?.tripRequest, lines),
    name: "",
    description: "",
    isActive: true,
    schedule: DEFAULT_SCHEDULE,
  };
}

export function templateValuesOf(dto: TemplateDto | undefined): OrderValues {
  const base = orderValuesOf(undefined);
  if (!dto) return base;
  return {
    ...base,
    transportOrderTypeId: dto.transportOrderTypeId ?? null,
    customer: relationOptionOf(dto.customerRelationId, dto.customerName),
    customerContactPersonId: dto.customerContactPersonId ?? null,
    customerReference: dto.customerReference ?? "",
    organizationUnitId: dto.organizationUnitId ?? null,
    internalRemarks: dto.internalRemarks ?? "",
    invoicingRemarks: dto.invoicingRemarks ?? "",
    attributes: dto.attributes ?? {},
    defaultStops: (dto.defaultStops ?? []).map(stopValuesOf),
    lines: (dto.lines ?? []).map(lineValuesOf),
    references: (dto.references ?? []).map((r) => ({ key: newKey("ref"), type: r.type, value: r.value, description: r.description ?? "" })),
    terms: termsValuesOf(dto.financialTerms),
    tripRequest: tripRequestValuesOf(dto.tripRequest),
    name: dto.name ?? "",
    description: dto.description ?? "",
    isActive: dto.isActive ?? true,
    schedule: {
      recurrence: (dto.schedule?.recurrence ?? 0) as 0 | 1 | 2,
      interval: numberToText(dto.schedule?.interval ?? 1),
      daysOfWeek: (dto.schedule?.daysOfWeek as number | undefined) ?? 0,
      dayOfMonth: numberToText(dto.schedule?.dayOfMonth),
      horizonDays: numberToText(dto.schedule?.horizonDays ?? 5),
      validFrom: dto.schedule?.validFrom ?? null,
      validTill: dto.schedule?.validTill ?? null,
    },
  };
}

/* ── To the DTOs ─────────────────────────────────────────────────────────────────────────────────────────────── */

const text = (value: string) => value.trim() || null;
const time = (value: string) => (value.trim() ? parseTimeOfDay(value) : null);

export function stopInputOf(stop: StopValues, mode: EditorMode): TransportOrderStopInputDto {
  return {
    id: stop.id,
    waypointTypeId: stop.waypointTypeId ?? "",
    relationId: stop.relationId ?? "",
    relationAddressId: stop.relationAddressId ?? "",
    contactPersonId: stop.contactPersonId,
    date: mode === "order" ? (stop.date ?? undefined) : undefined,
    timeSlotId: stop.timeSlotId,
    windowFrom: time(stop.windowFrom),
    windowTill: time(stop.windowTill),
    reference: text(stop.reference),
    remarks: text(stop.remarks),
    plannedDurationMin: parseInteger(stop.plannedDurationMin),
  };
}

export function templateStopInputOf(stop: StopValues): TemplateStopInputDto {
  return {
    waypointTypeId: stop.waypointTypeId ?? "",
    relationId: stop.relationId ?? "",
    relationAddressId: stop.relationAddressId ?? "",
    contactPersonId: stop.contactPersonId,
    dayOffset: parseInteger(stop.dayOffset) ?? 0,
    timeSlotId: stop.timeSlotId,
    windowFrom: time(stop.windowFrom),
    windowTill: time(stop.windowTill),
    reference: text(stop.reference),
    remarks: text(stop.remarks),
    plannedDurationMin: parseInteger(stop.plannedDurationMin),
  };
}

export function defaultStopInputOf(stop: StopValues): TransportOrderDefaultStopInputDto {
  return {
    waypointTypeId: stop.waypointTypeId ?? "",
    relationId: stop.relationId,
    relationAddressId: stop.relationAddressId,
    contactPersonId: stop.contactPersonId,
    dayOffset: parseInteger(stop.dayOffset) ?? 0,
    timeSlotId: stop.timeSlotId,
    windowFrom: time(stop.windowFrom),
    windowTill: time(stop.windowTill),
    plannedDurationMin: parseInteger(stop.plannedDurationMin),
  };
}

function lineCore(line: LineValues) {
  return {
    id: line.id,
    transportUnitId: line.transportUnitId ?? "",
    productId: line.productId,
    productRelationCodeId: line.productRelationCodeId,
    temperatureId: line.temperatureId,
    vehicleTypeId: line.vehicleTypeId,
    quantity: parseNumber(line.quantity) ?? 0,
    positions: parseNumber(line.positions),
    colli: parseNumber(line.colli),
    weightKg: parseNumber(line.weightKg),
    volumeM3: parseNumber(line.volumeM3),
    loadingMeters: parseNumber(line.loadingMeters),
    loadFactor: parseNumber(line.loadFactor),
    epq: parseNumber(line.epq),
    lengthCm: parseNumber(line.lengthCm),
    widthCm: parseNumber(line.widthCm),
    heightCm: parseNumber(line.heightCm),
    measurements: measurementsOf(line.weightMeasurements, line.volumeMeasurements),
    invoiceReference: text(line.invoiceReference),
    remarks: text(line.remarks),
    internalRemarks: text(line.internalRemarks),
    shipperInstructions: text(line.shipperInstructions),
    specialAgreements: text(line.specialAgreements),
    attachedDocuments: text(line.attachedDocuments),
    paymentInstruction: line.paymentInstruction,
    containerNumber: text(line.containerNumber),
    sizeOrIso: text(line.sizeOrIso),
    pinCode: text(line.pinCode),
    shippingCompany: text(line.shippingCompany),
    shipperRelationId: line.shipperRelation?.id ?? null,
    barcode: text(line.barcode),
    requiredTransportCharacteristicIds: line.requiredTransportCharacteristicIds,
    attributes: line.attributes,
  };
}

export function lineInputOf(line: LineValues): TransportOrderLineInputDto {
  return { ...lineCore(line), stops: line.stops.map((stop) => stopInputOf(stop, "order")) };
}

export function templateLineInputOf(line: LineValues): TemplateLineInputDto {
  return { ...lineCore(line), stops: line.stops.map(templateStopInputOf) };
}

export function termsInputOf(terms: TermsValues): TransportOrderFinancialTermsInputDto {
  return {
    currencyCode: terms.currencyCode.trim().toUpperCase() || "EUR",
    vatPercentageId: terms.vatPercentageId,
    paymentTermDays: parseInteger(terms.paymentTermDays),
    priceListIds: terms.priceListIds,
    multipleMatches: terms.multipleMatches,
    discountsOrSurcharges: terms.discountsOrSurcharges,
    fixedPriceAmount: parseNumber(terms.fixedPriceAmount),
    sendFreightDocumentsWithInvoice: terms.sendFreightDocumentsWithInvoice,
  };
}

/** Whether the unsaved lines can be priced (finding 1): at least one line, every line has a transport unit and at least
 * two stops, and every stop has a waypoint type, a relation and an address. Below that the preview is not asked for. */
export function linesArePriceable(lines: LineValues[]): boolean {
  return (
    lines.length > 0 &&
    lines.every((line) => !!line.transportUnitId && line.stops.length >= 2 && line.stops.every((stop) => !!stop.waypointTypeId && !!stop.relationId && !!stop.relationAddressId))
  );
}

/** An empty string is never an id: every `…Id` field that holds `""` goes to the server as null. */
function withoutEmptyIds<T>(value: T): T {
  if (Array.isArray(value)) return value.map(withoutEmptyIds) as T;
  if (value && typeof value === "object") {
    return Object.fromEntries(Object.entries(value).map(([key, v]) => [key, /Id$/.test(key) && v === "" ? null : withoutEmptyIds(v)])) as T;
  }
  return value;
}

/** The body of `preview-prices` for the unsaved order (pricing §4.3), or null while the lines cannot be priced yet. */
export function pricePreviewRequestOf(values: Pick<OrderValues, "customer" | "orderDate" | "terms" | "lines">): PricePreviewRequestDto | null {
  if (!linesArePriceable(values.lines)) return null;
  return withoutEmptyIds({ customerRelationId: values.customer?.id || null, orderDate: values.orderDate, financialTerms: termsInputOf(values.terms), lines: values.lines.map(lineInputOf) });
}

export function tripRequestInputOf(request: TripRequestValues): TransportOrderTripRequestInputDto | undefined {
  if (!request.enabled) return undefined;
  return {
    transportTripTypeId: request.transportTripTypeId ?? "",
    departureRelationId: request.departureRelation?.id ?? "",
    departureAddressId: request.departureAddressId ?? "",
    arrivalRelationId: request.arrivalRelation?.id ?? "",
    arrivalAddressId: request.arrivalAddressId ?? "",
    plannedDeparture: joinDateTime(request.departureDate, request.departureTime) ?? undefined,
    plannedArrival: joinDateTime(request.arrivalDate, request.arrivalTime),
    carrierRelationId: request.carrierRelation?.id ?? null,
    carrierContactPersonId: request.carrierContactPersonId,
    vehicleCombinationId: request.vehicleCombinationId,
    vehicleIds: request.vehicleIds,
    driverIds: request.driverIds,
    agreedPurchaseAmount: parseNumber(request.agreedPurchaseAmount),
    agreedPurchaseCurrencyCode: text(request.agreedPurchaseCurrencyCode)?.toUpperCase() ?? null,
  };
}

export function templateTripRequestInputOf(request: TripRequestValues): TemplateTripRequestInputDto | undefined {
  if (!request.enabled) return undefined;
  return {
    transportTripTypeId: request.transportTripTypeId ?? "",
    departureRelationId: request.departureRelation?.id ?? "",
    departureAddressId: request.departureAddressId ?? "",
    arrivalRelationId: request.arrivalRelation?.id ?? "",
    arrivalAddressId: request.arrivalAddressId ?? "",
    departureDayOffset: parseInteger(request.departureDayOffset) ?? 0,
    departureTime: time(request.departureTime) ?? undefined,
    arrivalDayOffset: parseInteger(request.arrivalDayOffset),
    arrivalTime: time(request.arrivalTime),
    carrierRelationId: request.carrierRelation?.id ?? null,
    carrierContactPersonId: request.carrierContactPersonId,
    vehicleCombinationId: request.vehicleCombinationId,
    vehicleIds: request.vehicleIds,
    driverIds: request.driverIds,
    agreedPurchaseAmount: parseNumber(request.agreedPurchaseAmount),
    agreedPurchaseCurrencyCode: text(request.agreedPurchaseCurrencyCode)?.toUpperCase() ?? null,
  };
}

export const referencesInputOf = (references: ReferenceValues[]) => references.map((r) => ({ type: r.type.trim(), value: r.value.trim(), description: text(r.description) }));

/** OrdersConsts: at most 50 references; type 32, value 128, description 256 characters. */
export const MAX_REFERENCES = 50;
export const REFERENCE_LIMITS = { type: 32, value: 128, description: 256 } as const;

export const newReference = (): ReferenceValues => ({ key: newKey("ref"), type: "", value: "", description: "" });

/** The customer of a stored order changed: the clerk is asked about the new customer's financial defaults (design 58, build note 7). */
export const customerChanged = (values: OrderValues, initial: OrderValues) => (values.customer?.id ?? null) !== (initial.customer?.id ?? null);

/** The freight items left off the Tevens trip, by id (or key for an unsaved item); null when the trip takes all. */
export function tripSelectionOf(values: Pick<OrderValues, "lines" | "tripRequest">): string[] | null {
  if (!values.tripRequest.enabled) return null;
  const excluded = values.lines.filter((line) => values.tripRequest.excludedLineKeys.includes(line.key)).map((line) => (line.id ?? line.key).toLowerCase());
  return excluded.length > 0 ? excluded.sort() : null;
}

/** The trip request's `lineIds` for the stored lines the save just wrote (by position: the server returns them in the
 * posted order): null when every item goes on the trip, else the ids of those that do. Never `[]` (that posts "every
 * line"): the validation refuses a trip without items first. */
export function tripLineIdsFor(values: Pick<OrderValues, "lines" | "tripRequest">, storedLines: readonly { id?: string | null }[]): string[] | null {
  if (tripSelectionOf(values) == null) return null;
  const ids = values.lines.flatMap((line, index) => (values.tripRequest.excludedLineKeys.includes(line.key) ? [] : storedLines[index]?.id ? [storedLines[index].id!] : []));
  return ids.length > 0 ? ids : null;
}

/** The ids a create (or a template save) posts on its lines: the stored id, or a client id for a new line, which the
 * server maps to the line's real id, so the trip request can name a new item (design 61 "Order page, legacy tab structure" 8). */
export function postedLineIdsOf(lines: readonly LineValues[], newId: () => string = () => crypto.randomUUID()): Map<string, string> {
  return new Map(lines.map((line) => [line.key, line.id ?? newId()]));
}

/** The trip request's `lineIds` over the posted ids: null when every item goes on the trip (never `[]`). */
export function tripLineIdsPosted(values: Pick<OrderValues, "lines" | "tripRequest">, posted: Map<string, string>): string[] | null {
  if (tripSelectionOf(values) == null) return null;
  const ids = values.lines.filter((line) => !values.tripRequest.excludedLineKeys.includes(line.key)).map((line) => posted.get(line.key)!);
  return ids.length > 0 ? ids : null;
}

/** Which sections of the order changed since it was loaded (each has its own command, design 58 API contract). */
export interface ChangedSections {
  header: boolean;
  defaultStops: boolean;
  lines: boolean;
  references: boolean;
  terms: boolean;
  tripRequest: boolean;
  attributes: boolean;
}

const headerOf = (v: OrderValues) => [v.transportOrderTypeId, v.customer?.id, v.customerContactPersonId, v.orderDate, v.customerReference, v.organizationUnitId, v.internalRemarks, v.invoicingRemarks];

export function changedSections(values: OrderValues, initial: OrderValues): ChangedSections {
  return {
    header: !isSameValue(headerOf(values), headerOf(initial)),
    defaultStops: !isSameValue(values.defaultStops.map(defaultStopInputOf), initial.defaultStops.map(defaultStopInputOf)),
    lines: !isSameValue(values.lines.map(lineInputOf), initial.lines.map(lineInputOf)),
    references: !isSameValue(referencesInputOf(values.references), referencesInputOf(initial.references)),
    terms: !isSameValue(termsInputOf(values.terms), termsInputOf(initial.terms)),
    tripRequest: !isSameValue([tripRequestInputOf(values.tripRequest), tripSelectionOf(values)], [tripRequestInputOf(initial.tripRequest), tripSelectionOf(initial)]),
    attributes: !isSameValue(values.attributes, initial.attributes),
  };
}

/** True when two versions of an order differ in what a user edits (any section), not only in derived values (the
 * summary a recalculation writes changes the concurrency stamp but none of these). */
export function contentDiffers(a: OrderValues, b: OrderValues): boolean {
  return Object.values(changedSections(a, b)).some(Boolean);
}

export type SectionKey = keyof ChangedSections;

/** The order in which a save runs the section commands. */
export const SECTION_ORDER: SectionKey[] = ["header", "defaultStops", "lines", "references", "terms", "tripRequest", "attributes"];

/** The form fields each section command writes. */
export const SECTION_FIELDS: Record<SectionKey, (keyof OrderValues)[]> = {
  header: ["transportOrderTypeId", "customer", "customerContactPersonId", "orderDate", "customerReference", "organizationUnitId", "internalRemarks", "invoicingRemarks"],
  defaultStops: ["defaultStops"],
  lines: ["lines"],
  references: ["references"],
  terms: ["terms"],
  tripRequest: ["tripRequest"],
  attributes: ["attributes"],
};

/** The section names a save report shows (UI-FB-007: the subject named). */
export const SECTION_LABEL_KEYS: Record<SectionKey, string> = {
  header: "Orders:OrderDetails",
  defaultStops: "Orders:DefaultStops",
  lines: "Orders:Lines",
  references: "Orders:References",
  terms: "Orders:FinancialTerms",
  tripRequest: "Orders:TripRequest",
  attributes: "Orders:Attributes",
};

/** One call of a save: the section command it runs and the sections it writes (the header call may carry the terms). */
export interface SaveStep {
  command: SectionKey;
  sections: SectionKey[];
}

/**
 * The calls a save of a stored order runs, in order. The terms travel inside the header call when the header changed
 * and the terms must be sent with it (a terms edit, or a customer change: the defaults the clerk took over or the terms
 * kept; design 58 build note 7), so one failure cannot leave half of that change saved; a terms-only change keeps its
 * own command.
 */
export function savePlan(changed: ChangedSections, customerChange: boolean): SaveStep[] {
  const sendTerms = changed.terms || (changed.header && customerChange);
  return SECTION_ORDER.flatMap((section): SaveStep[] => {
    if (section === "header") return changed.header ? [{ command: "header", sections: sendTerms ? ["header", "terms"] : ["header"] }] : [];
    if (section === "terms") return sendTerms && !changed.header ? [{ command: "terms", sections: ["terms"] }] : [];
    return changed[section] ? [{ command: section, sections: [section] }] : [];
  });
}

/**
 * The form after a save that stopped part-way: the saved sections take the server's values (new lines got their ids,
 * so a retry does not send them again as new), the other sections keep what the user typed and stay dirty against the
 * server's state, which becomes the baseline.
 */
export function mergeSavedSections(typed: OrderValues, server: OrderValues, saved: SectionKey[]): OrderValues {
  const merged = { ...typed } as Record<keyof OrderValues, unknown>;
  for (const section of saved) for (const field of SECTION_FIELDS[section]) merged[field] = server[field];
  return merged as unknown as OrderValues;
}

/* ── Validation ──────────────────────────────────────────────────────────────────────────────────────────────── */

/** The required-field flags of the order type (design 51) the editor marks (UI-FORM-002). */
export const REQUIRED_FLAG = {
  CustomerReference: 1,
  LoadingReference: 2,
  UnloadingReference: 4,
  LoadingTimeWindow: 8,
  UnloadingTimeWindow: 16,
  ProductPerLine: 32,
  TransportUnitPerLine: 64,
  QuantityPerLine: 128,
  WeightPerLine: 256,
  VolumePerLine: 512,
  LoadingMetersPerLine: 1024,
  Temperature: 2048,
} as const;

export const hasFlag = (flags: number | undefined, flag: number) => ((flags ?? 0) & flag) !== 0;

const NUMERIC_LINE_FIELDS: (keyof LineValues & string)[] = ["quantity", "positions", "colli", "weightKg", "volumeM3", "loadingMeters", "loadFactor", "epq", "lengthCm", "widthCm", "heightCm"];

/**
 * The client-side checks (UI-FORM-006): the fields a save needs (the API refuses without them) plus numeric shape.
 * The type's required flags are marks only: a Draft saves without them and Confirm reports `RequiredFieldMissing`
 * (design 58, "enforced on Confirm, not on Draft save").
 */
export function validateOrder(values: OrderValues, mode: EditorMode): FieldErrors<OrderValues> {
  const errors: FieldErrors<OrderValues> = {};
  if (!values.transportOrderTypeId) errors.transportOrderTypeId = "MasterData:Required";
  if (!values.customer) errors.customer = "MasterData:Required";
  if (mode === "order" && !values.orderDate) errors.orderDate = "MasterData:Required";
  if (mode === "template") {
    if (!values.name.trim()) errors.name = "MasterData:Required";
    const s = values.schedule;
    if (numberError(s.interval, { integer: true, min: 1, max: 52 }) || !s.interval.trim()) errors.schedule = "OpenTms.Orders:InvalidSchedule";
    if (numberError(s.horizonDays, { integer: true, min: 1, max: 60 }) || !s.horizonDays.trim()) errors.schedule = "OpenTms.Orders:InvalidSchedule";
    if (s.recurrence === 2 && (numberError(s.dayOfMonth, { integer: true, min: 1, max: 31 }) || !s.dayOfMonth.trim())) errors.schedule = "OpenTms.Orders:InvalidSchedule";
    if (s.recurrence === 1 && s.daysOfWeek === 0) errors.schedule = "Financial:DaysOfWeekRequiredHint";
    if (!s.validFrom) errors.schedule = "OpenTms.Orders:InvalidSchedule";
  }
  for (const line of values.lines) {
    if (!line.transportUnitId) errors.lines = "Orders:LineNeedsTransportUnit";
    for (const field of NUMERIC_LINE_FIELDS) if (numberError(line[field] as string)) errors.lines = "MasterData:InvalidNumber";
    for (const measurement of [...line.weightMeasurements, ...line.volumeMeasurements]) if (numberError(measurement, { min: 0 })) errors.lines = "MasterData:InvalidNumber";
    /* The freight items tab shows the first item's problem with the stop named (lineProblemText). */
    const stop = stopProblemsOf(line.stops, mode)[0];
    if (stop) errors.lines = stop.key;
  }
  for (const reference of values.references) {
    const type = reference.type.trim();
    const value = reference.value.trim();
    if (!type || !value || type.length > REFERENCE_LIMITS.type || value.length > REFERENCE_LIMITS.value || reference.description.trim().length > REFERENCE_LIMITS.description) errors.references = "Orders:ReferenceInvalid";
  }
  if (values.references.length > MAX_REFERENCES) errors.references = "Orders:ReferenceInvalid";
  if (numberError(values.terms.paymentTermDays, { integer: true, min: 0 }) || numberError(values.terms.fixedPriceAmount, { min: 0 })) errors.terms = "MasterData:InvalidNumber";
  if (values.tripRequest.enabled) {
    const r = values.tripRequest;
    if (!r.transportTripTypeId || !r.departureRelation || !r.departureAddressId || !r.arrivalRelation || !r.arrivalAddressId) errors.tripRequest = "Orders:TripRequestIncomplete";
    if (mode === "order" && !r.departureDate) errors.tripRequest = "Orders:TripRequestIncomplete";
    if ((r.departureTime.trim() && !parseTimeOfDay(r.departureTime)) || (r.arrivalTime.trim() && !parseTimeOfDay(r.arrivalTime))) errors.tripRequest = "Orders:InvalidTime";
    if (numberError(r.agreedPurchaseAmount, { min: 0 })) errors.tripRequest = "MasterData:InvalidNumber";
    if (values.lines.length > 0 && values.lines.every((line) => r.excludedLineKeys.includes(line.key))) errors.tripRequest = "Orders:TripRequestNoItems";
  }
  return errors;
}

/** What is wrong with a freight item (the list marks the item); a stop's problem names the stop. */
export interface LineProblem {
  key: string;
  stop?: StopProblem;
}

/** What is wrong with each freight item, by key: the first problem (design 61 "Order page, legacy tab structure" 3). */
export function lineProblemsOf(lines: LineValues[], mode: EditorMode = "order"): Map<string, LineProblem> {
  const problems = new Map<string, LineProblem>();
  for (const line of lines) {
    const stop = stopProblemsOf(line.stops, mode)[0];
    if (!line.transportUnitId) problems.set(line.key, { key: "Orders:LineNeedsTransportUnit" });
    else if (line.stops.length < MIN_STOPS) problems.set(line.key, { key: "Orders:LineNeedsTwoStopsHint" });
    else if (stop) problems.set(line.key, { key: stop.key, stop });
    else if (NUMERIC_LINE_FIELDS.some((field) => numberError(line[field] as string)) || [...line.weightMeasurements, ...line.volumeMeasurements].some((m) => numberError(m, { min: 0 }))) problems.set(line.key, { key: "MasterData:InvalidNumber" });
  }
  return problems;
}

/** The text of an item's problem: a stop's names the stop ("Stop 2 (Unload): …"). */
export function lineProblemText(t: (key: string) => string, line: Pick<LineValues, "stops">, problem: LineProblem): string {
  return problem.stop ? stopProblemText(t, line.stops, problem.stop) : t(problem.key);
}

/** Whether a stop's waypoint type loads or unloads, from the type's activity (the required marks per flag). */
export const isLoading = (activity: number | undefined) => activity === 1;
export const isUnloading = (activity: number | undefined) => activity === 2;
