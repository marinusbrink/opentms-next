import { describe, expect, it } from "vitest";

import type { RelationOption } from "@/domains/master-data/entities";
import type { TransportTripTypeDto } from "@/domains/planning-execution/reference-data";
import {
  MAX_TRIP_DRIVERS,
  MAX_TRIP_VEHICLES,
  NO_TRIP_TYPE_PRESET,
  mergeOptions,
  optionsOfIds,
  presetFieldsOf,
  switchTripTypePreset,
  timeText,
  tripTypePresetOf,
  type TripPresetFields,
  type TripTypePreset,
} from "@/domains/planning-execution/trip-type-presets";

/* The trip type's presets on screen (design 61 "Trip type defaults" 5 and build note 2): a type DTO as a preset; a type
 * switch changes the fields that still hold the previous preset and keeps the user's; carrier+contact and
 * relation+address switch as pairs (a replaced parent takes the new type's child), combination and vehicles together
 * only when both still hold the previous preset; drivers, departure time, execution, stop composition and first/last
 * stop rule each on their own. Risk class: Medium (screens and presets). */

const rel = (id: string, name = id.toUpperCase()): RelationOption => ({ id, code: "", name, city: null, countryCode: null, roles: 0 });

const typeDto = (extra: Partial<TransportTripTypeDto> = {}): TransportTripTypeDto => ({
  id: "tt-a",
  code: "A",
  defaultCarrierRelationId: "car-a",
  defaultCarrierRelationName: "Carrier A",
  defaultCarrierContactPersonId: "cp-a",
  defaultVehicleCombinationId: null,
  defaultVehicleIds: ["v1", "v2"],
  defaultVehicles: [
    { id: "v1", name: "DAF 1", licencePlate: "AB-12-CD", kind: 1, vehicleTypeName: "Tractor" },
    { id: "v2", name: "Trailer 2", licencePlate: null, kind: 2, vehicleTypeName: null },
  ],
  defaultDriverIds: ["d1"],
  defaultDrivers: [{ id: "d1", name: "Jan" }],
  defaultDepartureRelationId: "dep-a",
  defaultDepartureRelationName: "Depot A",
  defaultDepartureAddressId: "adr-dep-a",
  defaultDepartureTime: "06:30:00",
  defaultArrivalRelationId: "arr-a",
  defaultArrivalRelationName: "Hub A",
  defaultArrivalAddressId: "adr-arr-a",
  defaultExecution: 1,
  defaultStopCompositionMode: 2,
  defaultFirstLastStopRule: 3,
  ...extra,
});

/** A preset built by hand (each test names only what matters). */
const preset = (extra: Partial<TripTypePreset> = {}): TripTypePreset => ({ ...NO_TRIP_TYPE_PRESET, ...extra });

const A = preset({
  carrierRelation: rel("car-a"),
  carrierContactPersonId: "cp-a",
  vehicleCombinationId: "combi-a",
  driverIds: ["d1"],
  departureRelation: rel("dep-a"),
  departureAddressId: "adr-dep-a",
  departureTime: "06:00",
  arrivalRelation: rel("arr-a"),
  arrivalAddressId: "adr-arr-a",
  execution: 1,
  stopCompositionMode: 1,
  firstLastStopRule: 0,
});
const B = preset({
  carrierRelation: rel("car-b"),
  carrierContactPersonId: "cp-b",
  vehicleIds: ["v7", "v8"],
  driverIds: ["d2", "d3"],
  departureRelation: rel("dep-b"),
  departureAddressId: "adr-dep-b",
  departureTime: "07:15",
  arrivalRelation: rel("arr-b"),
  arrivalAddressId: "adr-arr-b",
  execution: 2,
  stopCompositionMode: 2,
  firstLastStopRule: 3,
});

/** Form values holding a preset (as a form after the preset was applied), plus a field the presets never touch. */
const holding = (p: TripPresetFields) => ({ ...presetFieldsOf(p), execution: p.execution ?? null, stopCompositionMode: p.stopCompositionMode ?? null, firstLastStopRule: p.firstLastStopRule ?? null, name: "Morning run" });

describe("tripTypePresetOf", () => {
  it("no type is no preset", () => {
    expect(tripTypePresetOf(undefined)).toBe(NO_TRIP_TYPE_PRESET);
    expect(tripTypePresetOf(null)).toBe(NO_TRIP_TYPE_PRESET);
  });

  it("maps every default of the type, with the names the pickers show and the time as HH:mm", () => {
    const p = tripTypePresetOf(typeDto());
    expect(p).toMatchObject({
      carrierRelation: { id: "car-a", name: "Carrier A" },
      carrierContactPersonId: "cp-a",
      vehicleCombinationId: null,
      vehicleIds: ["v1", "v2"],
      driverIds: ["d1"],
      departureRelation: { id: "dep-a", name: "Depot A" },
      departureAddressId: "adr-dep-a",
      departureTime: "06:30",
      arrivalRelation: { id: "arr-a", name: "Hub A" },
      arrivalAddressId: "adr-arr-a",
      /* The type's carrier subcontracts the trip over its own-fleet default (PO 2026-10-29, point 2). */
      execution: 2,
      stopCompositionMode: 2,
      firstLastStopRule: 3,
    });
    expect(p.vehicles).toEqual([
      { id: "v1", name: "DAF 1", licencePlate: "AB-12-CD", kind: 1, vehicleTypeName: "Tractor" },
      { id: "v2", name: "Trailer 2", licencePlate: null, kind: 2, vehicleTypeName: null },
    ]);
    expect(p.drivers).toEqual([{ id: "d1", name: "Jan", code: "" }]);
  });

  it("a combination excludes vehicles; a contact needs its carrier and an address its relation", () => {
    const p = tripTypePresetOf(
      typeDto({ defaultVehicleCombinationId: "combi-1", defaultVehicleCombinationName: "Combi 1", defaultCarrierRelationId: null, defaultDepartureRelationId: null, defaultArrivalRelationId: null }),
    );
    expect(p.vehicleCombinationId).toBe("combi-1");
    expect(p.vehicleCombinationName).toBe("Combi 1");
    expect(p.vehicleIds).toEqual([]);
    expect(p.carrierRelation).toBeNull();
    expect(p.carrierContactPersonId).toBeNull();
    expect(p.execution).toBe(1);
    expect(p.departureAddressId).toBeNull();
    expect(p.arrivalAddressId).toBeNull();
  });

  it("an empty type presets nothing; ids without their id list come from the detail lists, capped at the maximum", () => {
    expect(tripTypePresetOf({ id: "tt-empty" })).toEqual({ ...NO_TRIP_TYPE_PRESET });
    const many = tripTypePresetOf(
      typeDto({
        defaultVehicleIds: null,
        defaultVehicles: ["a", "b", "c", "d"].map((id) => ({ id, name: id })),
        defaultDriverIds: null,
        defaultDrivers: ["x", "y", "z", "w"].map((id) => ({ id, name: id })),
      }),
    );
    expect(many.vehicleIds).toEqual(["a", "b", "c"].slice(0, MAX_TRIP_VEHICLES));
    expect(many.driverIds).toEqual(["x", "y", "z"].slice(0, MAX_TRIP_DRIVERS));
  });

  it("timeText cuts the seconds and maps none to empty", () => {
    expect(timeText("07:30:00")).toBe("07:30");
    expect(timeText(null)).toBe("");
    expect(timeText(undefined)).toBe("");
  });
});

describe("switchTripTypePreset", () => {
  it("a first choice (against no type) fills the empty fields and keeps what the user already typed", () => {
    const current = { ...holding(NO_TRIP_TYPE_PRESET), driverIds: ["mine"], departureTime: "05:00" };
    const result = switchTripTypePreset(current, NO_TRIP_TYPE_PRESET, B);
    expect(result).toMatchObject({
      carrierRelation: { id: "car-b" },
      carrierContactPersonId: "cp-b",
      vehicleCombinationId: null,
      vehicleIds: ["v7", "v8"],
      driverIds: ["mine"],
      departureRelation: { id: "dep-b" },
      departureAddressId: "adr-dep-b",
      departureTime: "05:00",
      arrivalRelation: { id: "arr-b" },
      arrivalAddressId: "adr-arr-b",
      execution: 2,
      stopCompositionMode: 2,
      firstLastStopRule: 3,
      name: "Morning run",
    });
  });

  it("untouched fields all take the next type's preset; fields outside the preset stay", () => {
    const result = switchTripTypePreset(holding(A), A, B);
    expect(presetFieldsOf(result)).toEqual(presetFieldsOf(B));
    expect(result.name).toBe("Morning run");
  });

  it("does not mutate its input", () => {
    const current = holding(A);
    const copy = structuredClone(current);
    switchTripTypePreset(current, A, B);
    expect(current).toEqual(copy);
  });

  it("drivers, departure time, execution, stop composition and first/last stop rule switch each on their own", () => {
    const current = { ...holding(A), driverIds: ["d9"], departureTime: "09:45", execution: 2 as const };
    const result = switchTripTypePreset(current, A, B);
    expect(result.driverIds).toEqual(["d9"]);
    expect(result.departureTime).toBe("09:45");
    expect(result.execution).toBe(2);
    /* the untouched neighbours still switch */
    expect(result.stopCompositionMode).toBe(2);
    expect(result.firstLastStopRule).toBe(3);
    expect(result.carrierRelation?.id).toBe("car-b");
  });

  it("a user-changed enum that happens to equal the next preset is kept (only previous-preset values switch)", () => {
    const current = { ...holding(A), stopCompositionMode: 2 as const };
    expect(switchTripTypePreset(current, A, { ...B, stopCompositionMode: 1 }).stopCompositionMode).toBe(2);
  });

  it("an untouched departure time with surrounding spaces still counts as the preset", () => {
    expect(switchTripTypePreset({ ...holding(A), departureTime: " 06:00 " }, A, B).departureTime).toBe("07:15");
  });

  describe("carrier and contact as a pair", () => {
    it("a carrier the user changed stays with the user's contact", () => {
      const current = { ...holding(A), carrierRelation: rel("car-x"), carrierContactPersonId: "cp-x" };
      const result = switchTripTypePreset(current, A, B);
      expect(result.carrierRelation?.id).toBe("car-x");
      expect(result.carrierContactPersonId).toBe("cp-x");
    });

    it("a replaced carrier takes the new type's contact, even when the user had picked another contact of the old carrier", () => {
      const current = { ...holding(A), carrierContactPersonId: "cp-a2" };
      const result = switchTripTypePreset(current, A, B);
      expect(result.carrierRelation?.id).toBe("car-b");
      expect(result.carrierContactPersonId).toBe("cp-b");
    });

    it("a next type without a carrier clears the carrier and its contact", () => {
      const result = switchTripTypePreset(holding(A), A, { ...B, carrierRelation: null, carrierContactPersonId: null });
      expect(result.carrierRelation).toBeNull();
      expect(result.carrierContactPersonId).toBeNull();
    });

    it("the same carrier on both types: an untouched contact switches, a changed contact stays", () => {
      const next = { ...B, carrierRelation: rel("car-a"), carrierContactPersonId: "cp-b" };
      expect(switchTripTypePreset(holding(A), A, next).carrierContactPersonId).toBe("cp-b");
      expect(switchTripTypePreset({ ...holding(A), carrierContactPersonId: "cp-mine" }, A, next).carrierContactPersonId).toBe("cp-mine");
    });

    it("ids compare without case (a GUID from another source)", () => {
      const current = { ...holding(A), carrierRelation: rel("CAR-A") };
      expect(switchTripTypePreset(current, A, B).carrierRelation?.id).toBe("car-b");
    });
  });

  describe("relation and address as a pair (departure and arrival)", () => {
    it("a relation the user changed stays with the user's address; the other side still switches", () => {
      const current = { ...holding(A), departureRelation: rel("dep-x"), departureAddressId: "adr-x" };
      const result = switchTripTypePreset(current, A, B);
      expect(result.departureRelation?.id).toBe("dep-x");
      expect(result.departureAddressId).toBe("adr-x");
      expect(result.arrivalRelation?.id).toBe("arr-b");
      expect(result.arrivalAddressId).toBe("adr-arr-b");
    });

    it("a replaced relation takes the new type's address even when the user picked another address of the old relation", () => {
      const current = { ...holding(A), arrivalAddressId: "adr-arr-a2" };
      const result = switchTripTypePreset(current, A, B);
      expect(result.arrivalRelation?.id).toBe("arr-b");
      expect(result.arrivalAddressId).toBe("adr-arr-b");
    });

    it("the same relation on both types: an untouched address switches, a changed address stays", () => {
      const next = { ...B, departureRelation: rel("dep-a"), departureAddressId: "adr-dep-b" };
      expect(switchTripTypePreset(holding(A), A, next).departureAddressId).toBe("adr-dep-b");
      expect(switchTripTypePreset({ ...holding(A), departureAddressId: "adr-mine" }, A, next).departureAddressId).toBe("adr-mine");
    });
  });

  describe("combination and vehicles together", () => {
    it("both untouched: the combination gives way to the next type's vehicles, and back", () => {
      const toB = switchTripTypePreset(holding(A), A, B);
      expect(toB.vehicleCombinationId).toBeNull();
      expect(toB.vehicleIds).toEqual(["v7", "v8"]);
      const backToA = switchTripTypePreset(toB, B, A);
      expect(backToA.vehicleCombinationId).toBe("combi-a");
      expect(backToA.vehicleIds).toEqual([]);
    });

    it("vehicles the user picked (which cleared the combination) stay, and no combination comes back", () => {
      const current = { ...holding(A), vehicleCombinationId: null, vehicleIds: ["v1"] };
      const result = switchTripTypePreset(current, A, { ...B, vehicleIds: [], vehicleCombinationId: "combi-b" });
      expect(result.vehicleCombinationId).toBeNull();
      expect(result.vehicleIds).toEqual(["v1"]);
    });

    it("a combination the user changed stays", () => {
      const current = { ...holding(A), vehicleCombinationId: "combi-mine" };
      const result = switchTripTypePreset(current, A, B);
      expect(result.vehicleCombinationId).toBe("combi-mine");
      expect(result.vehicleIds).toEqual([]);
    });

    it("vehicles in another order count as changed", () => {
      const prev = { ...B };
      const current = { ...holding(B), vehicleIds: ["v8", "v7"] };
      expect(switchTripTypePreset(current, prev, A).vehicleIds).toEqual(["v8", "v7"]);
    });
  });

  it("a form without the enum fields (the Tevens trip) gets none added", () => {
    const withoutEnums: TripPresetFields = {
      carrierRelation: null,
      carrierContactPersonId: null,
      vehicleCombinationId: null,
      vehicleIds: [],
      driverIds: [],
      departureRelation: null,
      departureAddressId: null,
      departureTime: "",
      arrivalRelation: null,
      arrivalAddressId: null,
    };
    const result = switchTripTypePreset(withoutEnums, NO_TRIP_TYPE_PRESET, B);
    expect("execution" in result).toBe(false);
    expect("stopCompositionMode" in result).toBe(false);
    expect("firstLastStopRule" in result).toBe(false);
    expect(result.carrierRelation?.id).toBe("car-b");
  });
});

describe("presetFieldsOf, mergeOptions, optionsOfIds", () => {
  it("presetFieldsOf keeps only the preset fields, the enums only when the form has them", () => {
    const fields = presetFieldsOf({ ...holding(A) });
    expect(Object.keys(fields).sort()).toEqual(
      ["arrivalAddressId", "arrivalRelation", "carrierContactPersonId", "carrierRelation", "departureAddressId", "departureRelation", "departureTime", "driverIds", "execution", "firstLastStopRule", "stopCompositionMode", "vehicleCombinationId", "vehicleIds"].sort(),
    );
    const { execution: _e, stopCompositionMode: _s, firstLastStopRule: _f, ...withoutEnums } = presetFieldsOf(A);
    void _e;
    void _s;
    void _f;
    expect("execution" in presetFieldsOf(withoutEnums)).toBe(false);
  });

  it("mergeOptions: by id without case, the later list winning", () => {
    expect(mergeOptions([{ id: "a", name: "old" }, { id: "b", name: "B" }], [{ id: "A", name: "new" }])).toEqual([
      { id: "A", name: "new" },
      { id: "b", name: "B" },
    ]);
  });

  it("optionsOfIds: known names in the ids' order, the fallback for an unknown id", () => {
    const known = [{ id: "v1", name: "DAF" }, { id: "v2", name: "MAN" }];
    expect(optionsOfIds(["V2", "v9", "v1"], known, (id) => ({ id, name: id }))).toEqual([
      { id: "v2", name: "MAN" },
      { id: "v9", name: "v9" },
      { id: "v1", name: "DAF" },
    ]);
  });
});
