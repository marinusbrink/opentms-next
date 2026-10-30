import { describe, expect, it } from "vitest";

import { newTripCreateDtoOf, newTripValues, validateNewTrip, withTripDate, type NewTripValues } from "@/apps/transport/trips/new-trip-form";
import type { RelationOption } from "@/domains/master-data/entities";

/* The New trip dialog's form model (design 61 "Trip type defaults" 4, build note 3): today as trip date and planned
 * departure date, the planned departure date following the trip date until the user changes it, the required fields,
 * and the create input: empty lists and enums left to the server's fill, a contact only with its carrier, an address
 * only with its relation, never vehicles beside a combination. Risk class: Medium. */

const rel = (id: string): RelationOption => ({ id, code: "", name: id, city: null, countryCode: null, roles: 0 });
const valid = (extra: Partial<NewTripValues> = {}): NewTripValues => ({ ...newTripValues("2026-10-29"), transportTripTypeId: "tt-1", departureTime: "06:30", ...extra });

describe("newTripValues", () => {
  it("today as trip date and planned departure date; everything else empty for the type to preset", () => {
    expect(newTripValues("2026-10-29")).toEqual({
      transportTripTypeId: null,
      tripDate: "2026-10-29",
      departureDate: "2026-10-29",
      departureTime: "",
      name: "",
      execution: null,
      carrierRelation: null,
      carrierContactPersonId: null,
      vehicleCombinationId: null,
      vehicleIds: [],
      driverIds: [],
      departureRelation: null,
      departureAddressId: null,
      arrivalRelation: null,
      arrivalAddressId: null,
      stopCompositionMode: null,
      firstLastStopRule: null,
    });
  });
});

describe("withTripDate", () => {
  it("the planned departure date follows the trip date while it still is the trip date", () => {
    const moved = withTripDate(newTripValues("2026-10-29"), "2026-11-03");
    expect(moved.tripDate).toBe("2026-11-03");
    expect(moved.departureDate).toBe("2026-11-03");
    expect(withTripDate(moved, "2026-11-04").departureDate).toBe("2026-11-04");
  });

  it("an empty planned departure date follows too", () => {
    expect(withTripDate({ ...newTripValues("2026-10-29"), departureDate: null }, "2026-11-03").departureDate).toBe("2026-11-03");
  });

  it("a planned departure date the user changed stays", () => {
    const changed = { ...newTripValues("2026-10-29"), departureDate: "2026-10-30" };
    const moved = withTripDate(changed, "2026-11-03");
    expect(moved.tripDate).toBe("2026-11-03");
    expect(moved.departureDate).toBe("2026-10-30");
  });
});

describe("validateNewTrip", () => {
  it("a complete form has no errors", () => {
    expect(validateNewTrip(valid())).toEqual({});
  });

  it("type, trip date, planned departure date and time are required", () => {
    expect(validateNewTrip({ ...newTripValues("2026-10-29"), tripDate: null, departureDate: null })).toEqual({
      transportTripTypeId: "MasterData:Required",
      tripDate: "MasterData:Required",
      departureDate: "MasterData:Required",
      departureTime: "MasterData:Required",
    });
    expect(validateNewTrip(valid({ departureTime: "   " }))).toEqual({ departureTime: "MasterData:Required" });
  });

  it("an invalid time is refused; 0630 and 6:30 are times", () => {
    for (const time of ["25:00", "12:60", "noon", "6"]) expect(validateNewTrip(valid({ departureTime: time }))).toEqual({ departureTime: "Orders:InvalidTime" });
    for (const time of ["0630", "6:30", "23:59"]) expect(validateNewTrip(valid({ departureTime: time }))).toEqual({});
  });

  it("nothing else is required: carrier, resources, departure and arrival are the server's to fill", () => {
    expect(validateNewTrip(valid({ name: "", execution: null, stopCompositionMode: null, firstLastStopRule: null }))).toEqual({});
  });
});

describe("newTripCreateDtoOf", () => {
  it("an untouched form leaves every fillable field empty for the server (null or absent), with the planned departure in the browser's zone", () => {
    const dto = newTripCreateDtoOf(valid());
    expect(dto).toEqual({
      transportTripTypeId: "tt-1",
      tripDate: "2026-10-29",
      plannedDeparture: new Date(2026, 9, 29, 6, 30).toISOString(),
      name: null,
      carrierRelationId: null,
      carrierContactPersonId: null,
      vehicleCombinationId: null,
      vehicleIds: [],
      driverIds: [],
      departureRelationId: null,
      departureAddressId: null,
      arrivalRelationId: null,
      arrivalAddressId: null,
    });
    /* enums not chosen are not sent at all (the server takes the type's) */
    expect(dto.execution).toBeUndefined();
    expect(dto.stopCompositionMode).toBeUndefined();
    expect(dto.firstLastStopRule).toBeUndefined();
  });

  it("a filled form sends every field", () => {
    const dto = newTripCreateDtoOf(
      valid({
        name: "  Morning run ",
        execution: 2,
        carrierRelation: rel("car-1"),
        carrierContactPersonId: "cp-1",
        vehicleIds: ["v1", "v2"],
        driverIds: ["d1"],
        departureRelation: rel("dep-1"),
        departureAddressId: "adr-dep-1",
        arrivalRelation: rel("arr-1"),
        arrivalAddressId: "adr-arr-1",
        stopCompositionMode: 2,
        firstLastStopRule: 3,
        departureDate: "2026-10-30",
        departureTime: "0715",
      }),
    );
    expect(dto).toEqual({
      transportTripTypeId: "tt-1",
      tripDate: "2026-10-29",
      plannedDeparture: new Date(2026, 9, 30, 7, 15).toISOString(),
      name: "Morning run",
      execution: 2,
      carrierRelationId: "car-1",
      carrierContactPersonId: "cp-1",
      vehicleCombinationId: null,
      vehicleIds: ["v1", "v2"],
      driverIds: ["d1"],
      departureRelationId: "dep-1",
      departureAddressId: "adr-dep-1",
      arrivalRelationId: "arr-1",
      arrivalAddressId: "adr-arr-1",
      stopCompositionMode: 2,
      firstLastStopRule: 3,
    });
  });

  it("never a contact without its carrier, an address without its relation, or vehicles beside a combination", () => {
    const dto = newTripCreateDtoOf(valid({ carrierContactPersonId: "cp-1", departureAddressId: "adr-1", arrivalAddressId: "adr-2", vehicleCombinationId: "combi-1", vehicleIds: ["v1"] }));
    expect(dto.carrierContactPersonId).toBeNull();
    expect(dto.departureAddressId).toBeNull();
    expect(dto.arrivalAddressId).toBeNull();
    expect(dto.vehicleCombinationId).toBe("combi-1");
    expect(dto.vehicleIds).toEqual([]);
  });
});
