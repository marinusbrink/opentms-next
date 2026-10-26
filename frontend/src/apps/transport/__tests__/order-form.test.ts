import { describe, expect, it } from "vitest";

import {
  addDays,
  changedSections,
  joinDateTime,
  lineInputOf,
  mergeSavedSections,
  newLine,
  orderValuesOf,
  pricePreviewRequestOf,
  tripRequestWithDefaults,
  defaultTripTypeIdOf,
  EMPTY_TRIP_REQUEST,
  newStop,
  savePlan,
  splitDateTime,
  stopValuesOf,
  templateLineInputOf,
  templateValuesOf,
  termsInputOf,
  tripRequestInputOf,
  validateOrder,
  emptyEntry,
  entryErrors,
  entryWaypointsOf,
  itemFromEntry,
  lineProblemsOf,
  nextEntry,
  postedLineIdsOf,
  tripLineIdsFor,
  tripLineIdsPosted,
  tripSelectionOf,
  type OrderValues,
} from "@/apps/transport/orders/order-form";
import type { TransportOrderDto } from "@/domains/orders/transport-orders";

/* The order editor's form model (design 61): the DTO ↔ values mappers, the section diff a save runs on, the
 * client-side validation (the API's rules stay the API's) and the date helpers. Risk class: Medium. */

const stored: TransportOrderDto = {
  id: "o1",
  number: "TO-2026-00001",
  status: 2,
  transportOrderTypeId: "type-1",
  customerRelationId: "rel-1",
  customerName: "Acme",
  orderDate: "2026-10-25",
  customerReference: "REF",
  concurrencyStamp: "s1",
  defaultStops: [
    { waypointTypeId: "wp-load", waypointTypeName: "Load", dayOffset: 0, position: 1 },
    { waypointTypeId: "wp-unload", waypointTypeName: "Unload", dayOffset: 1, position: 2 },
  ],
  lines: [
    {
      id: "l1",
      position: 1,
      transportUnitId: "unit-1",
      quantity: 2,
      weightKg: 100.5,
      paymentInstruction: 0,
      stops: [
        { id: "s1", position: 1, waypointTypeId: "wp-load", relationId: "rel-1", relationAddressId: "adr-1", date: "2026-10-25", windowFrom: "08:00:00", windowTill: "12:00:00" },
        { id: "s2", position: 2, waypointTypeId: "wp-unload", relationId: "rel-2", relationAddressId: "adr-2", date: "2026-10-26" },
      ],
    },
  ],
  financialTerms: { currencyCode: "EUR", vatPercentageId: "vat-1", paymentTermDays: 30, priceListIds: ["pl-1"], multipleMatches: 1, discountsOrSurcharges: [{ discountOrSurchargeId: "ds-1", source: 1 }], fixedPriceAmount: null, sendFreightDocumentsWithInvoice: true },
  tripRequest: undefined,
};

describe("orderValuesOf / lineInputOf", () => {
  it("maps the record to form values and back without losing fields", () => {
    const values = orderValuesOf(stored);
    expect(values.customer).toMatchObject({ id: "rel-1", name: "Acme" });
    expect(values.lines).toHaveLength(1);
    expect(values.lines[0].quantity).toBe("2");
    expect(values.lines[0].stops[0].windowFrom).toBe("08:00");
    expect(values.defaultStops.map((s) => s.dayOffset)).toEqual(["0", "1"]);
    expect(values.terms.discountsOrSurcharges).toEqual([{ discountOrSurchargeId: "ds-1", source: 1 }]);

    const input = lineInputOf(values.lines[0]);
    expect(input).toMatchObject({ id: "l1", transportUnitId: "unit-1", quantity: 2, weightKg: 100.5, paymentInstruction: 0 });
    expect(input.stops?.[0]).toMatchObject({ id: "s1", waypointTypeId: "wp-load", relationId: "rel-1", relationAddressId: "adr-1", date: "2026-10-25", windowFrom: "08:00:00", windowTill: "12:00:00" });
    expect(input.stops?.[1].windowFrom).toBeNull();
    expect(termsInputOf(values.terms)).toEqual({ currencyCode: "EUR", vatPercentageId: "vat-1", paymentTermDays: 30, priceListIds: ["pl-1"], multipleMatches: 1, discountsOrSurcharges: [{ discountOrSurchargeId: "ds-1", source: 1 }], fixedPriceAmount: null, sendFreightDocumentsWithInvoice: true });
  });

  it("a new item from the fast entry row: From with the date, time and reference, To on the same date; quantity 1; no stop pattern", () => {
    const acme = { id: "rel-1", code: "ACME", name: "Acme", city: "Utrecht", countryCode: "NL", roles: 1 };
    const beta = { id: "rel-2", code: "BETA", name: "Beta", city: null, countryCode: null, roles: 1 };
    const entry = { ...emptyEntry("2026-10-25"), from: { relation: acme, addressId: "adr-1", city: "Utrecht" }, to: { relation: beta, addressId: "adr-2", city: "Gouda" }, time: "730", transportUnitId: "unit-1", transportUnitName: "Pallet", weightKg: "120", reference: " LOAD-1 " };
    const line = itemFromEntry(entry, { from: { id: "wp-load", name: "Load" }, to: { id: "wp-unload", name: "Unload" } });
    expect(line).toMatchObject({ id: null, transportUnitId: "unit-1", quantity: "1", weightKg: "120" });
    expect(line.stops.map((s) => [s.waypointTypeId, s.relationId, s.relationAddressId, s.city, s.date, s.windowFrom, s.reference])).toEqual([
      ["wp-load", "rel-1", "adr-1", "Utrecht", "2026-10-25", "07:30", "LOAD-1"],
      ["wp-unload", "rel-2", "adr-2", "Gouda", "2026-10-25", "", ""],
    ]);
    expect(lineInputOf(line).stops?.map((s) => s.date)).toEqual(["2026-10-25", "2026-10-25"]);
    /* A template item keeps day offsets. */
    const templateLine = itemFromEntry({ ...entry, date: null, dayOffset: "2" }, { from: null, to: null });
    expect(templateLineInputOf(templateLine).stops?.map((s) => s.dayOffset)).toEqual([2, 2]);
    /* Enter keeps From, the date, the time and the unit for the next item. */
    expect(nextEntry(entry)).toMatchObject({ from: entry.from, to: { relation: null }, date: "2026-10-25", time: "730", transportUnitId: "unit-1", quantity: "", weightKg: "", reference: "" });
    expect(newLine().stops).toEqual([]);
    expect(stopValuesOf({ waypointTypeId: "wp", relationId: "r", relationAddressId: "a", dayOffset: 3 }).dayOffset).toBe("3");
  });

  it("the entry needs From, To, the unit and (orders) a date; a bad time or number is named", () => {
    expect(Object.keys(entryErrors(emptyEntry(null), "order")).sort()).toEqual(["date", "from", "to", "transportUnitId"]);
    expect(Object.keys(entryErrors(emptyEntry(null), "template")).sort()).toEqual(["from", "to", "transportUnitId"]);
    expect(entryErrors({ ...emptyEntry("2026-10-25"), time: "25:00", quantity: "x" }, "order")).toMatchObject({ time: "Orders:InvalidTime", quantity: "MasterData:InvalidNumber" });
  });

  it("the item's waypoint types: the order type's first and last default waypoint, else the first Load and Unload type", () => {
    const types = [{ id: "wp-via", name: "Via", activity: 3 }, { id: "wp-unload", name: "Unload", activity: 2 }, { id: "wp-load", name: "Load", activity: 1 }];
    expect(entryWaypointsOf([{ waypointTypeId: "b", name: "B", position: 2 }, { waypointTypeId: "a", name: "A", position: 1 }, { waypointTypeId: "c", name: "C", position: 3 }], types)).toEqual({ from: { id: "a", name: "A" }, to: { id: "c", name: "C" } });
    expect(entryWaypointsOf([], types)).toEqual({ from: { id: "wp-load", name: "Load" }, to: { id: "wp-unload", name: "Unload" } });
    expect(entryWaypointsOf([{ waypointTypeId: "a", name: "A", position: 1 }], types)).toEqual({ from: { id: "a", name: "A" }, to: { id: "wp-unload", name: "Unload" } });
    expect(entryWaypointsOf(undefined, undefined)).toEqual({ from: null, to: null });
  });
});

describe("Tevens trip per freight item (lineIds)", () => {
  const two = (): TransportOrderDto => ({ ...stored, lines: [stored.lines![0], { ...stored.lines![0], id: "l2", position: 2 }], tripRequest: { transportTripTypeId: "tt", departureRelationId: "rel-1", departureAddressId: "adr-1", arrivalRelationId: "rel-2", arrivalAddressId: "adr-2", plannedDeparture: "2026-10-25T06:00:00Z", vehicleIds: [], driverIds: [], lineIds: null } });

  it("null reads as every item; a list as those items; a stored [] as none (the validation then asks to choose)", () => {
    expect(orderValuesOf(two()).tripRequest.excludedLineKeys).toEqual([]);
    const some = orderValuesOf({ ...two(), tripRequest: { ...two().tripRequest!, lineIds: ["L2"] } });
    expect(some.tripRequest.excludedLineKeys).toEqual([some.lines[0].key]);
    const none = orderValuesOf({ ...two(), tripRequest: { ...two().tripRequest!, lineIds: [] } });
    expect(none.tripRequest.excludedLineKeys).toEqual(none.lines.map((l) => l.key));
    expect(validateOrder(none, "order").tripRequest).toBe("Orders:TripRequestNoItems");
  });

  it("an item ticked off is a trip-request change; the posted lineIds name the items on the trip, never []", () => {
    const initial = orderValuesOf(two());
    const values = { ...initial, tripRequest: { ...initial.tripRequest, excludedLineKeys: [initial.lines[0].key] } };
    expect(changedSections(values, initial)).toMatchObject({ tripRequest: true, lines: false });
    expect(tripSelectionOf(values)).toEqual(["l1"]);
    expect(tripLineIdsFor(values, [{ id: "l1" }, { id: "l2" }])).toEqual(["l2"]);
    expect(tripLineIdsFor(initial, [{ id: "l1" }, { id: "l2" }])).toBeNull();
    /* A new order posts client ids on its new lines and names them. */
    const fresh = { ...values, lines: [...values.lines, { ...newLine(), key: "new-1" }] };
    let n = 0;
    const posted = postedLineIdsOf(fresh.lines, () => `client-${++n}`);
    expect([...posted.values()]).toEqual(["l1", "l2", "client-1"]);
    expect(tripLineIdsPosted(fresh, posted)).toEqual(["l2", "client-1"]);
    const allOff = { ...fresh, tripRequest: { ...fresh.tripRequest, excludedLineKeys: fresh.lines.map((l) => l.key) } };
    expect(tripLineIdsPosted(allOff, posted)).toBeNull();
    expect(validateOrder(allOff, "order").tripRequest).toBe("Orders:TripRequestNoItems");
  });

  it("the trip's prefill takes the items on the trip only", () => {
    const initial = orderValuesOf(two());
    const lines = [{ ...initial.lines[0], stops: [{ ...initial.lines[0].stops[0], relationId: "x", relationName: "X" }, initial.lines[0].stops[1]] }, initial.lines[1]];
    const request = tripRequestWithDefaults({ ...EMPTY_TRIP_REQUEST, enabled: true, excludedLineKeys: [lines[0].key] }, lines, "order", null);
    expect(request.departureRelation?.id).toBe("rel-1");
  });
});

describe("lineProblemsOf (the list marks an incomplete item)", () => {
  it("names a missing unit, fewer than two stops and an incomplete stop", () => {
    const values = orderValuesOf(stored);
    const ok = values.lines[0];
    const problems = lineProblemsOf([ok, { ...ok, key: "a", transportUnitId: null }, { ...ok, key: "b", stops: ok.stops.slice(0, 1) }, { ...ok, key: "c", stops: [ok.stops[0], { ...ok.stops[1], relationAddressId: null }] }]);
    expect([...problems.entries()]).toEqual([
      ["a", "Orders:LineNeedsTransportUnit"],
      ["b", "Orders:LineNeedsTwoStopsHint"],
      ["c", "Orders:StopIncomplete"],
    ]);
  });
});

describe("changedSections", () => {
  it("names only the sections that differ from the loaded record", () => {
    const initial = orderValuesOf(stored);
    const same = orderValuesOf(stored);
    expect(Object.values(changedSections(same, initial)).every((v) => v === false)).toBe(true);

    const header: OrderValues = { ...initial, customerReference: "NEW" };
    expect(changedSections(header, initial)).toMatchObject({ header: true, lines: false, terms: false });

    const lines: OrderValues = { ...initial, lines: [{ ...initial.lines[0], quantity: "3" }] };
    expect(changedSections(lines, initial)).toMatchObject({ header: false, lines: true });

    const terms: OrderValues = { ...initial, terms: { ...initial.terms, paymentTermDays: "14" } };
    expect(changedSections(terms, initial)).toMatchObject({ terms: true, lines: false });

    const trip: OrderValues = { ...initial, tripRequest: { ...initial.tripRequest, enabled: true } };
    expect(changedSections(trip, initial)).toMatchObject({ tripRequest: true });
  });

  it("whitespace-only edits are no change (UI-FORM-009)", () => {
    const initial = orderValuesOf(stored);
    expect(changedSections({ ...initial, internalRemarks: "   " }, initial).header).toBe(false);
  });
});

describe("validateOrder", () => {
  it("requires the type, the customer and (orders) the date; the type's required flags are marks only", () => {
    const values = orderValuesOf(undefined);
    expect(validateOrder(values, "order")).toMatchObject({ transportOrderTypeId: "MasterData:Required", customer: "MasterData:Required", orderDate: "MasterData:Required" });
    expect(validateOrder(orderValuesOf(stored), "order")).toEqual({});
  });

  it("reports an incomplete stop, a line without unit and a bad time on the Lines tab", () => {
    const values = orderValuesOf(stored);
    const broken: OrderValues = { ...values, lines: [{ ...values.lines[0], transportUnitId: null, stops: [{ ...values.lines[0].stops[0], windowFrom: "25:99" }] }] };
    expect(validateOrder(broken, "order").lines).toBeDefined();
  });

  it("checks the template's name and schedule in template mode", () => {
    const values = templateValuesOf({ id: "t1", name: "", transportOrderTypeId: "type-1", customerRelationId: "rel-1", schedule: { recurrence: 1, interval: 0, daysOfWeek: 0, horizonDays: 5, validFrom: "2026-10-01" } });
    const errors = validateOrder(values, "template");
    expect(errors.name).toBe("MasterData:Required");
    expect(errors.schedule).toBeDefined();
    expect(errors.orderDate).toBeUndefined();
  });

  it("a trip request needs its type, places and departure date", () => {
    const values = orderValuesOf(stored);
    const withTrip: OrderValues = { ...values, tripRequest: { ...values.tripRequest, enabled: true } };
    expect(validateOrder(withTrip, "order").tripRequest).toBe("Orders:TripRequestIncomplete");
    expect(tripRequestInputOf(values.tripRequest)).toBeUndefined();
  });
});

describe("date helpers", () => {
  it("addDays is a calendar operation", () => {
    expect(addDays("2026-10-31", 1)).toBe("2026-11-01");
    expect(addDays("2026-03-28", 3)).toBe("2026-03-31");
    expect(addDays(null, 1)).toBeNull();
  });

  it("splitDateTime and joinDateTime round-trip in the browser's zone", () => {
    const iso = joinDateTime("2026-10-25", "14:30")!;
    expect(splitDateTime(iso)).toEqual({ date: "2026-10-25", time: "14:30" });
    expect(joinDateTime(null, "14:30")).toBeNull();
    expect(splitDateTime(null)).toEqual({ date: null, time: "" });
  });
});

describe("savePlan / mergeSavedSections (one call for header and terms; a save that stops part-way)", () => {
  const none = { header: false, defaultStops: false, lines: false, references: false, terms: false, tripRequest: false, attributes: false };

  it("puts the terms into the header call when both change or the customer changed, and keeps the terms command for a terms-only change", () => {
    expect(savePlan({ ...none, header: true, terms: true }, false)).toEqual([{ command: "header", sections: ["header", "terms"] }]);
    expect(savePlan({ ...none, header: true }, true)).toEqual([{ command: "header", sections: ["header", "terms"] }]);
    expect(savePlan({ ...none, header: true }, false)).toEqual([{ command: "header", sections: ["header"] }]);
    expect(savePlan({ ...none, terms: true }, false)).toEqual([{ command: "terms", sections: ["terms"] }]);
    expect(savePlan({ ...none, lines: true, attributes: true, header: true, tripRequest: true }, false).map((s) => s.command)).toEqual(["header", "lines", "tripRequest", "attributes"]);
    expect(savePlan(none, true)).toEqual([]);
  });

  it("takes the saved sections from the server and keeps the others as typed", () => {
    const server = orderValuesOf(stored);
    const typed: OrderValues = { ...server, customerReference: "TYPED", lines: [{ ...server.lines[0], id: null, quantity: "9" }], attributes: { a: "1" } };
    const fromServer: OrderValues = { ...server, customerReference: "REF-SERVER", lines: [{ ...server.lines[0], id: "l-new", quantity: "9" }] };
    const merged = mergeSavedSections(typed, fromServer, ["header", "lines"]);
    expect(merged.customerReference).toBe("REF-SERVER");
    expect(merged.lines[0].id).toBe("l-new");
    expect(merged.attributes).toEqual({ a: "1" });
    expect(changedSections(merged, fromServer)).toEqual({ ...none, attributes: true });
  });
});

describe("pricePreviewRequestOf (finding 1)", () => {
  const complete = () => orderValuesOf({ ...stored, lines: [{ id: "l1", position: 1, transportUnitId: "unit-1", quantity: 1, paymentInstruction: 0, stops: [
    { id: "s1", position: 1, waypointTypeId: "wp-load", relationId: "rel-1", relationAddressId: "adr-1", date: "2026-10-25" },
    { id: "s2", position: 2, waypointTypeId: "wp-unload", relationId: "rel-2", relationAddressId: "adr-2", date: "2026-10-26" },
  ] }] } as TransportOrderDto);

  it("builds the request for priceable lines and sends null, never an empty string, for an empty id", () => {
    const values = complete();
    values.terms = { ...values.terms, vatPercentageId: "" as unknown as null };
    const request = pricePreviewRequestOf(values)!;
    expect(request.lines![0].transportUnitId).toBe("unit-1");
    expect(request.customerRelationId).toBe("rel-1");
    expect(request.financialTerms!.vatPercentageId).toBeNull();
  });

  it("is null without lines, without a transport unit, with fewer than two stops or a stop without relation or address", () => {
    const base = complete();
    expect(pricePreviewRequestOf({ ...base, lines: [] })).toBeNull();
    expect(pricePreviewRequestOf({ ...base, lines: [{ ...base.lines[0], transportUnitId: null }] })).toBeNull();
    expect(pricePreviewRequestOf({ ...base, lines: [{ ...base.lines[0], stops: base.lines[0].stops.slice(0, 1) }] })).toBeNull();
    expect(pricePreviewRequestOf({ ...base, lines: [{ ...base.lines[0], stops: [base.lines[0].stops[0], { ...base.lines[0].stops[1], relationAddressId: null }] }] })).toBeNull();
    expect(pricePreviewRequestOf({ ...base, lines: [{ ...base.lines[0], stops: [{ ...base.lines[0].stops[0], relationId: null }, base.lines[0].stops[1]] }] })).toBeNull();
  });
});

describe("tripRequestWithDefaults (finding 4)", () => {
  const stop = (relationId: string, extra: Record<string, string> = {}) => ({ ...newStop("2026-10-25"), relationId, relationName: relationId.toUpperCase(), relationAddressId: `adr-${relationId}`, ...extra });
  const lines = (stops: ReturnType<typeof stop>[][]) => stops.map((s) => ({ ...newLine(), stops: s }));

  it("fills only the empty fields; a template takes the first stop's day offset and window start", () => {
    const template = tripRequestWithDefaults({ ...EMPTY_TRIP_REQUEST, enabled: true }, lines([[stop("a", { dayOffset: "1", windowFrom: "08:00" }), stop("b")]]), "template", "tt-1");
    expect(template).toMatchObject({ transportTripTypeId: "tt-1", departureDayOffset: "1", departureTime: "08:00", departureAddressId: "adr-a", arrivalAddressId: "adr-b" });

    const filled = tripRequestWithDefaults({ ...EMPTY_TRIP_REQUEST, enabled: true, transportTripTypeId: "mine", departureDate: "2026-11-01", departureTime: "" }, lines([[stop("a", { windowFrom: "08:00" }), stop("b")]]), "order", "tt-1");
    expect(filled).toMatchObject({ transportTripTypeId: "mine", departureDate: "2026-11-01", departureTime: "" });
  });

  it("without lines nothing but the type is filled", () => {
    expect(tripRequestWithDefaults({ ...EMPTY_TRIP_REQUEST, enabled: true }, [], "order", null)).toEqual({ ...EMPTY_TRIP_REQUEST, enabled: true });
  });

  it("the default type when it is active, else the only active type", () => {
    expect(defaultTripTypeIdOf("b", ["a", "b"])).toBe("b");
    expect(defaultTripTypeIdOf("archived", ["a", "b"])).toBeNull();
    expect(defaultTripTypeIdOf(null, ["a"])).toBe("a");
    expect(defaultTripTypeIdOf(null, [])).toBeNull();
  });
});
