import { describe, expect, it } from "vitest";

import { EMPTY_TRIP_REQUEST, newLine, newStop, tripRequestForTypeChange, tripRequestWithDefaults, type TripRequestValues } from "@/apps/transport/orders/order-form";
import type { RelationOption } from "@/domains/master-data/entities";
import { NO_TRIP_TYPE_PRESET, type TripTypePreset } from "@/domains/planning-execution/trip-type-presets";

/* The Tevens trip's prefill with the trip type's defaults (design 61 "Trip type defaults" 5, build note 2): the type's
 * departure/arrival win over the order's first/last stop; the stops fill only what the type leaves empty; a stop's
 * window start is the departure time only when the type has none; a template takes the first stop's day offset; a type
 * switch changes what still holds the previous preset (with the stops' fallback) and never the date or day offset.
 * Without a preset `tripRequestWithDefaults` behaves as before (order-form.test.ts "finding 4"). Risk class: Medium. */

const rel = (id: string): RelationOption => ({ id, code: "", name: id.toUpperCase(), city: null, countryCode: null, roles: 0 });
const stop = (relationId: string, extra: Record<string, string> = {}) => ({ ...newStop("2026-11-02"), relationId, relationName: relationId.toUpperCase(), relationAddressId: `adr-${relationId}`, ...extra });
/* Two items: a → b and c → d; the trip's departure is a (first stop of the first item), its arrival d. */
const twoItems = () => [newLine([stop("a", { windowFrom: "08:00", dayOffset: "2" }), stop("b")]), newLine([stop("c", { windowFrom: "10:00", dayOffset: "3" }), stop("d")])];
const enabled = (extra: Partial<TripRequestValues> = {}): TripRequestValues => ({ ...EMPTY_TRIP_REQUEST, enabled: true, ...extra });
const preset = (extra: Partial<TripTypePreset> = {}): TripTypePreset => ({ ...NO_TRIP_TYPE_PRESET, ...extra });

const FULL = preset({
  carrierRelation: rel("car-t"),
  carrierContactPersonId: "cp-t",
  vehicleIds: ["v1"],
  driverIds: ["d1", "d2"],
  departureRelation: rel("dep-t"),
  departureAddressId: "adr-dep-t",
  departureTime: "06:00",
  arrivalRelation: rel("arr-t"),
  arrivalAddressId: "adr-arr-t",
  execution: 2,
  stopCompositionMode: 2,
  firstLastStopRule: 1,
});

describe("tripRequestWithDefaults with the type's preset", () => {
  it("the type's departure, arrival and time win over the stops; the date still comes from the first stop", () => {
    const result = tripRequestWithDefaults(enabled(), twoItems(), "order", "tt-1", FULL);
    expect(result).toMatchObject({
      transportTripTypeId: "tt-1",
      departureRelation: { id: "dep-t" },
      departureAddressId: "adr-dep-t",
      arrivalRelation: { id: "arr-t" },
      arrivalAddressId: "adr-arr-t",
      departureDate: "2026-11-02",
      departureTime: "06:00",
      carrierRelation: { id: "car-t" },
      carrierContactPersonId: "cp-t",
      vehicleCombinationId: null,
      vehicleIds: ["v1"],
      driverIds: ["d1", "d2"],
    });
  });

  it("the Tevens trip has no enum fields: the type's execution, stop composition and first/last stop rule are not added", () => {
    const result = tripRequestWithDefaults(enabled(), twoItems(), "order", "tt-1", FULL);
    expect("execution" in result).toBe(false);
    expect("stopCompositionMode" in result).toBe(false);
    expect("firstLastStopRule" in result).toBe(false);
  });

  it("what the type leaves empty comes from the stops: arrival from the last stop, the time from the first stop's window start", () => {
    const result = tripRequestWithDefaults(enabled(), twoItems(), "order", "tt-1", preset({ departureRelation: rel("dep-t"), departureAddressId: "adr-dep-t" }));
    expect(result).toMatchObject({ departureRelation: { id: "dep-t" }, departureAddressId: "adr-dep-t", arrivalRelation: { id: "d" }, arrivalAddressId: "adr-d", departureDate: "2026-11-02", departureTime: "08:00" });
  });

  it("a type's departure relation without an address leaves the address empty (never the first stop's address)", () => {
    const result = tripRequestWithDefaults(enabled(), twoItems(), "order", "tt-1", preset({ departureRelation: rel("dep-t") }));
    expect(result.departureRelation?.id).toBe("dep-t");
    expect(result.departureAddressId).toBeNull();
  });

  it("a type with only a time: departure and arrival from the stops, the type's time instead of the window start", () => {
    const result = tripRequestWithDefaults(enabled(), twoItems(), "order", "tt-1", preset({ departureTime: "05:30" }));
    expect(result).toMatchObject({ departureRelation: { id: "a" }, departureAddressId: "adr-a", arrivalRelation: { id: "d" }, departureDate: "2026-11-02", departureTime: "05:30" });
  });

  it("a template takes the first stop's day offset with the type's time", () => {
    const result = tripRequestWithDefaults(enabled(), twoItems(), "template", "tt-1", FULL);
    expect(result).toMatchObject({ departureDayOffset: "2", departureTime: "06:00", departureRelation: { id: "dep-t" } });
    const noTime = tripRequestWithDefaults(enabled(), twoItems(), "template", "tt-1", preset());
    expect(noTime).toMatchObject({ departureDayOffset: "2", departureTime: "08:00" });
  });

  it("only the items on the trip count for the stops' fallback", () => {
    const lines = twoItems();
    const result = tripRequestWithDefaults(enabled({ excludedLineKeys: [lines[0].key] }), lines, "order", "tt-1", preset({ arrivalRelation: rel("arr-t") }));
    expect(result).toMatchObject({ departureRelation: { id: "c" }, departureTime: "10:00", arrivalRelation: { id: "arr-t" } });
  });

  it("fields the user already filled stay; the preset fills only the empty ones", () => {
    const mine = enabled({ transportTripTypeId: "tt-mine", carrierRelation: rel("car-mine"), carrierContactPersonId: null, driverIds: ["d9"], departureTime: "11:00", departureDate: "2026-12-01" });
    const result = tripRequestWithDefaults(mine, twoItems(), "order", "tt-1", FULL);
    expect(result).toMatchObject({ transportTripTypeId: "tt-mine", carrierRelation: { id: "car-mine" }, carrierContactPersonId: null, driverIds: ["d9"], departureTime: "11:00", departureDate: "2026-12-01", vehicleIds: ["v1"], departureRelation: { id: "dep-t" } });
  });

  it("without a preset it behaves as before: the stops give departure, arrival, date and window start; nothing else is set", () => {
    const lines = twoItems();
    const withNull = tripRequestWithDefaults(enabled(), lines, "order", "tt-1", null);
    const omitted = tripRequestWithDefaults(enabled(), lines, "order", "tt-1");
    expect(withNull).toEqual(omitted);
    expect(omitted).toEqual({
      ...enabled(),
      transportTripTypeId: "tt-1",
      departureRelation: { id: "a", code: "", name: "A", city: null, countryCode: null, roles: 0 },
      departureAddressId: "adr-a",
      arrivalRelation: { id: "d", code: "", name: "D", city: null, countryCode: null, roles: 0 },
      arrivalAddressId: "adr-d",
      departureDate: "2026-11-02",
      departureTime: "08:00",
    });
  });
});

describe("tripRequestForTypeChange", () => {
  const A = preset({ departureRelation: rel("dep-a"), departureAddressId: "adr-dep-a", departureTime: "06:00", carrierRelation: rel("car-a"), driverIds: ["d1"] });
  const B = preset({ arrivalRelation: rel("arr-b"), arrivalAddressId: "adr-arr-b", vehicleCombinationId: "combi-b" });

  it("untouched fields move from type A to type B; where B has nothing the stops' fallback comes back", () => {
    const lines = twoItems();
    const before = tripRequestWithDefaults(enabled(), lines, "order", "tt-a", A);
    const after = tripRequestForTypeChange({ ...before, transportTripTypeId: "tt-b" }, lines, "order", A, B);
    expect(after).toMatchObject({
      transportTripTypeId: "tt-b",
      departureRelation: { id: "a" },
      departureAddressId: "adr-a",
      departureTime: "08:00",
      arrivalRelation: { id: "arr-b" },
      arrivalAddressId: "adr-arr-b",
      carrierRelation: null,
      driverIds: [],
      vehicleCombinationId: "combi-b",
      vehicleIds: [],
      departureDate: "2026-11-02",
    });
  });

  it("fields the user changed stay; the date, the day offset and the agreed price never switch", () => {
    const lines = twoItems();
    const before = { ...tripRequestWithDefaults(enabled(), lines, "order", "tt-a", A), departureRelation: rel("dep-x"), departureAddressId: "adr-x", departureTime: "09:30", departureDate: "2026-11-20", agreedPurchaseAmount: "400" };
    const after = tripRequestForTypeChange(before, lines, "order", A, B);
    expect(after).toMatchObject({ departureRelation: { id: "dep-x" }, departureAddressId: "adr-x", departureTime: "09:30", departureDate: "2026-11-20", agreedPurchaseAmount: "400", arrivalRelation: { id: "arr-b" } });
  });

  it("from fields filled by the stops only (no type known yet) the first type's preset takes over", () => {
    const lines = twoItems();
    const fromStops = tripRequestWithDefaults(enabled(), lines, "order", null, null);
    const after = tripRequestForTypeChange({ ...fromStops, transportTripTypeId: "tt-a" }, lines, "order", NO_TRIP_TYPE_PRESET, A);
    expect(after).toMatchObject({ departureRelation: { id: "dep-a" }, departureAddressId: "adr-dep-a", departureTime: "06:00", arrivalRelation: { id: "d" }, carrierRelation: { id: "car-a" }, driverIds: ["d1"] });
  });

  it("a template keeps its day offset on a switch", () => {
    const lines = twoItems();
    const before = { ...tripRequestWithDefaults(enabled(), lines, "template", "tt-a", A), departureDayOffset: "4" };
    const after = tripRequestForTypeChange(before, lines, "template", A, B);
    expect(after.departureDayOffset).toBe("4");
    expect(after.departureRelation?.id).toBe("a");
  });
});
