import { describe, expect, it } from "vitest";

import { numberError, parseInteger, parseNumber } from "@/apps/master-data/components/fields";
import { isValidRange } from "@/apps/master-data/editors/GeographicalRegionEditor";
import { localizedNameFromMap, localizedNameToMap, resolveLocalizedName, validateLocalizedName, withTextForCulture } from "@/domains/master-data/localized-name";

/* Pure helpers of the Master data app. Risk class: Low. */

describe("localized-name", () => {
  it("round-trips a map, dropping empty texts", () => {
    const values = localizedNameFromMap({ nl: "Nederland", en: "Netherlands" });
    expect(values).toEqual({ nl: "Nederland", en: "Netherlands" });
    expect(localizedNameToMap({ nl: "  ", en: "Netherlands" })).toEqual({ en: "Netherlands" });
  });

  it("requires the default-culture text only", () => {
    expect(validateLocalizedName({ nl: "x", en: "" })).toBe("MasterData:Required");
    expect(validateLocalizedName({ nl: "", en: "x" })).toBeUndefined();
  });

  it("writes a single Name field to the user's language and fills the default culture once", () => {
    expect(withTextForCulture({ en: "Euro" }, "nl-NL", "Euro (nl)")).toEqual({ en: "Euro", nl: "Euro (nl)" });
    expect(withTextForCulture(undefined, "nl", "Euro")).toEqual({ nl: "Euro", en: "Euro" });
    expect(withTextForCulture({ en: "Euro", nl: "Euro (nl)" }, "nl", "")).toEqual({ en: "Euro" });
    expect(withTextForCulture({ en: "Euro", nl: "x" }, "en", "Euro coin")).toEqual({ en: "Euro coin", nl: "x" });
  });

  it("resolves like the server: culture, language, default, first", () => {
    expect(resolveLocalizedName({ nl: "Nederland", en: "Netherlands" }, "nl-NL")).toBe("Nederland");
    expect(resolveLocalizedName({ en: "Netherlands" }, "nl")).toBe("Netherlands");
    expect(resolveLocalizedName({ de: "Niederlande" }, "nl")).toBe("Niederlande");
    expect(resolveLocalizedName(null, "nl")).toBe("");
  });
});

describe("number fields", () => {
  it("parses decimals with a comma or a dot and keeps empty as null", () => {
    expect(parseNumber("12,5")).toBe(12.5);
    expect(parseNumber("12.5")).toBe(12.5);
    expect(parseNumber("")).toBeNull();
    expect(Number.isNaN(parseNumber("abc"))).toBe(true);
  });

  it("validates integers and bounds", () => {
    expect(parseInteger("2")).toBe(2);
    expect(Number.isNaN(parseInteger("2.5"))).toBe(true);
    expect(numberError("5", { integer: true, min: 0, max: 4 })).toBe("MasterData:InvalidNumber");
    expect(numberError("", { integer: true })).toBeUndefined();
    expect(numberError("3", { integer: true, min: 0, max: 4 })).toBeUndefined();
  });
});

describe("postal-code range rule (editor side)", () => {
  const base = { key: "k", id: null, countryId: "nl", fromNumeric: null, tillNumeric: null, prefix: null };
  it("accepts a numeric range or a prefix, never both, never neither", () => {
    expect(isValidRange({ ...base, fromNumeric: 1000, tillNumeric: 1999 })).toBe(true);
    expect(isValidRange({ ...base, prefix: "SW" })).toBe(true);
    expect(isValidRange({ ...base, prefix: "SW", fromNumeric: 1 })).toBe(false);
    expect(isValidRange({ ...base })).toBe(false);
    expect(isValidRange({ ...base, fromNumeric: 2000, tillNumeric: 1999 })).toBe(false);
    expect(isValidRange({ ...base, countryId: null, prefix: "SW" })).toBe(false);
  });
});
