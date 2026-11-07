import { describe, expect, it } from "vitest";

import {
  isStopDetailDirty,
  stopActualsDto,
  stopCoordinates,
  stopDetailValues,
  stopUpdateDto,
  validateStopDetail,
  type StopDetailValues,
} from "@/domains/planning-execution/components/stop-detail-form";

/* The pure part of the stop detail window (design 74 §3): the form's values, the dirty compare, the validation and the two
 * requests. `record-stop-actuals` and `UpdateStopDto` treat null as "keep", so a stored value cannot be emptied here and only
 * what changed is sent. Risk class: High (planning data: actual times drive the trip's progress and the calculations). */

const CANNOT_BE_EMPTIED = "PlanningExecution:StopValueCannotBeEmptied";
const iso = (d: number, h: number, m = 0) => new Date(2026, 10, d, h, m).toISOString();

const VALUES: StopDetailValues = {
  arrivalDate: null,
  arrivalTime: "",
  departureDate: null,
  departureTime: "",
  remarks: "",
  plannedDurationMin: "",
  relationId: "r1",
  relationName: "Relation 1",
  relationAddressId: "a1",
};
const values = (patch: Partial<StopDetailValues> = {}): StopDetailValues => ({ ...VALUES, ...patch });

describe("stopDetailValues", () => {
  it("splits the actual times into local date and time", () => {
    const form = stopDetailValues({ actualArrival: iso(10, 8, 5), actualDeparture: iso(11, 17, 30), remarks: "Ring", plannedDurationMin: 45, relationId: "r1", relationName: "Rel", relationAddressId: "a1" });
    expect(form).toEqual({ arrivalDate: "2026-11-10", arrivalTime: "08:05", departureDate: "2026-11-11", departureTime: "17:30", remarks: "Ring", plannedDurationMin: "45", relationId: "r1", relationName: "Rel", relationAddressId: "a1" });
  });

  it("is empty for a stop without values; the stop's own name stands in for the relation's", () => {
    expect(stopDetailValues({ name: "Depot" })).toEqual({ ...VALUES, relationId: null, relationName: "Depot", relationAddressId: null });
    expect(stopDetailValues({ actualArrival: "not a date" }).arrivalDate).toBeNull();
  });
});

describe("isStopDetailDirty", () => {
  it("is clean for equal values", () => {
    expect(isStopDetailDirty(values(), values())).toBe(false);
  });

  it("sees every editable field", () => {
    for (const patch of [
      { arrivalDate: "2026-11-10" },
      { arrivalTime: "08:00" },
      { departureDate: "2026-11-10" },
      { departureTime: "08:00" },
      { remarks: "x" },
      { plannedDurationMin: "5" },
      { relationId: "r2" },
      { relationAddressId: "a2" },
    ]) {
      expect(isStopDetailDirty(values(), values(patch)), JSON.stringify(patch)).toBe(true);
    }
  });

  it("ignores surrounding white space and a relation's name that follows its id", () => {
    expect(isStopDetailDirty(values({ remarks: "x", arrivalTime: "08:00", plannedDurationMin: "5" }), values({ remarks: " x ", arrivalTime: " 08:00", plannedDurationMin: "5 " }))).toBe(false);
    expect(isStopDetailDirty(values(), values({ relationName: "Other spelling" }))).toBe(false);
  });
});

describe("validateStopDetail", () => {
  const initial = values();

  it("accepts an untouched form and complete times", () => {
    expect(validateStopDetail(initial, initial)).toEqual({});
    expect(validateStopDetail(initial, values({ arrivalDate: "2026-11-10", arrivalTime: "08:00", departureDate: "2026-11-10", departureTime: "1730", plannedDurationMin: "0" }))).toEqual({});
  });

  it("refuses an unreadable time", () => {
    expect(validateStopDetail(initial, values({ arrivalDate: "2026-11-10", arrivalTime: "25:00" }))).toEqual({ arrivalTime: "Orders:InvalidTime" });
    expect(validateStopDetail(initial, values({ departureDate: "2026-11-10", departureTime: "soon" }))).toEqual({ departureTime: "Orders:InvalidTime" });
  });

  it("wants a date with a time", () => {
    expect(validateStopDetail(initial, values({ arrivalTime: "08:00" }))).toEqual({ arrivalDate: "MasterData:Required" });
    expect(validateStopDetail(initial, values({ departureTime: "08:00" }))).toEqual({ departureDate: "MasterData:Required" });
  });

  it("refuses to empty a stored actual: null keeps it on the server", () => {
    const stored = values({ arrivalDate: "2026-11-10", arrivalTime: "08:00", departureDate: "2026-11-10", departureTime: "09:00" });
    expect(validateStopDetail(stored, values({ departureDate: "2026-11-10", departureTime: "09:00" }))).toEqual({ arrivalDate: CANNOT_BE_EMPTIED });
    expect(validateStopDetail(stored, values({ arrivalDate: "2026-11-10", arrivalTime: "08:00" }))).toEqual({ departureDate: CANNOT_BE_EMPTIED });
  });

  it("refuses a duration that is no non-negative whole number", () => {
    for (const text of ["abc", "-1", "1.5"]) {
      expect(validateStopDetail(initial, values({ plannedDurationMin: text })).plannedDurationMin, text).toBe("MasterData:InvalidNumber");
    }
  });

  it("refuses to empty a stored duration, not one that was never there", () => {
    expect(validateStopDetail(values({ plannedDurationMin: "30" }), values()).plannedDurationMin).toBe(CANNOT_BE_EMPTIED);
    expect(validateStopDetail(values(), values())).toEqual({});
  });
});

describe("stopUpdateDto: only what changed", () => {
  it("is null when nothing it carries changed, also when only the actuals did", () => {
    expect(stopUpdateDto(values(), values())).toBeNull();
    expect(stopUpdateDto(values(), values({ arrivalDate: "2026-11-10", arrivalTime: "08:00" }))).toBeNull();
    expect(stopUpdateDto(values({ remarks: "x" }), values({ remarks: " x " }))).toBeNull();
  });

  it("sends trimmed remarks alone, also when they are emptied", () => {
    expect(stopUpdateDto(values(), values({ remarks: " Ring " }))).toEqual({ remarks: "Ring" });
    expect(stopUpdateDto(values({ remarks: "Ring" }), values())).toEqual({ remarks: "" });
  });

  it("sends the duration alone, as a number", () => {
    expect(stopUpdateDto(values({ plannedDurationMin: "30" }), values({ plannedDurationMin: "45" }))).toEqual({ plannedDurationMin: 45 });
  });

  it("leaves the relation and address out unless they changed: the server refuses them on a stop with legs", () => {
    const dto = stopUpdateDto(values(), values({ remarks: "x" }));
    expect(dto).not.toHaveProperty("relationId");
    expect(dto).not.toHaveProperty("relationAddressId");
  });

  it("sends an address alone, and a relation with its address", () => {
    expect(stopUpdateDto(values(), values({ relationAddressId: "a2" }))).toEqual({ relationAddressId: "a2" });
    expect(stopUpdateDto(values(), values({ relationId: "r2", relationAddressId: null }))).toEqual({ relationId: "r2", relationAddressId: null });
    expect(stopUpdateDto(values(), values({ relationId: "r2" }))).toEqual({ relationId: "r2", relationAddressId: "a1" });
  });
});

describe("stopActualsDto: only what changed", () => {
  const instant = (d: number, h: number, m = 0) => new Date(2026, 10, d, h, m).toISOString();

  it("is null when neither time changed, whatever else did", () => {
    const stored = values({ arrivalDate: "2026-11-10", arrivalTime: "08:00" });
    expect(stopActualsDto(stored, { ...stored, remarks: "x" })).toBeNull();
    expect(stopActualsDto(stored, { ...stored, arrivalTime: " 08:00 " })).toBeNull();
  });

  it("sends the changed arrival, and null for the unchanged departure so it keeps its seconds", () => {
    const stored = values({ arrivalDate: "2026-11-10", arrivalTime: "08:00", departureDate: "2026-11-10", departureTime: "09:00" });
    expect(stopActualsDto(stored, { ...stored, arrivalTime: "08:15" })).toEqual({ actualArrival: instant(10, 8, 15), actualDeparture: null, cumulativeDistanceM: null });
  });

  it("sends the changed departure, and null for the unchanged arrival", () => {
    const stored = values({ arrivalDate: "2026-11-10", arrivalTime: "08:00" });
    expect(stopActualsDto(stored, { ...stored, departureDate: "2026-11-11", departureTime: "06:30" })).toEqual({ actualArrival: null, actualDeparture: instant(11, 6, 30), cumulativeDistanceM: null });
  });

  it("sends both when both changed", () => {
    expect(stopActualsDto(values(), values({ arrivalDate: "2026-11-10", arrivalTime: "08:00", departureDate: "2026-11-10", departureTime: "09:00" }))).toEqual({ actualArrival: instant(10, 8), actualDeparture: instant(10, 9), cumulativeDistanceM: null });
  });
});

describe("stopCoordinates", () => {
  it("gives usable coordinates, 0 on one axis included", () => {
    expect(stopCoordinates({ latitude: 51.5, longitude: 5.1 })).toEqual({ latitude: 51.5, longitude: 5.1 });
    expect(stopCoordinates({ latitude: 0, longitude: 5.1 })).toEqual({ latitude: 0, longitude: 5.1 });
  });

  it("is null for missing, unreadable, out-of-range and null-island values", () => {
    for (const [latitude, longitude] of [[null, 5], [51, null], [undefined, undefined], [Number.NaN, 5], [51, Number.POSITIVE_INFINITY], [91, 5], [-91, 5], [51, 181], [51, -181], [0, 0]]) {
      expect(stopCoordinates({ latitude, longitude } as never), `${latitude},${longitude}`).toBeNull();
    }
  });
});
