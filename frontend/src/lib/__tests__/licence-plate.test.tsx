import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { LicencePlateCell } from "@/components/ui/grid-cells";
import { formatLicencePlate, isLicencePlateShape, normalizeLicencePlate } from "@/lib/format";

/* The licence-plate formatter of design 48 (UI-FMT): the server stores a plate normalised (upper case, letters and
 * digits only) and the UI shows Dutch plates by their sidecode, every other country as stored. The same rules as
 * the server's VehicleRegistration (backend VehicleAppServiceTests). Risk class: Medium (display of every vehicle
 * list, lookup and editor). */

describe("normalizeLicencePlate", () => {
  it.each([
    ["12-abc-3", "12ABC3"],
    [" ab 12 34 ", "AB1234"],
    ["12.34.ab", "1234AB"],
    ["v_123·bb", "V123BB"],
    ["", ""],
    [null, ""],
    [undefined, ""],
  ])("%s → %s", (typed, stored) => {
    expect(normalizeLicencePlate(typed)).toBe(stored);
  });
});

describe("isLicencePlateShape", () => {
  it("accepts letters, digits and separators up to 16 characters once normalised, and an empty plate", () => {
    expect(isLicencePlateShape("12-ABC-3")).toBe(true);
    expect(isLicencePlateShape("1-abc-123")).toBe(true);
    expect(isLicencePlateShape("")).toBe(true);
    expect(isLicencePlateShape(null)).toBe(true);
    expect(isLicencePlateShape("ABCDEFGHJKLMNPRS")).toBe(true);
  });

  it("refuses other characters and plates longer than the column", () => {
    expect(isLicencePlateShape("12#AB")).toBe(false);
    expect(isLicencePlateShape("AB/12")).toBe(false);
    expect(isLicencePlateShape("ABCDEFGHJKLMNPRST")).toBe(false);
  });
});

describe("formatLicencePlate — Dutch sidecodes", () => {
  it.each([
    ["XX9999", "AB1234", "AB-12-34"], // 1
    ["9999XX", "1234AB", "12-34-AB"], // 2
    ["99XX99", "12AB34", "12-AB-34"], // 3
    ["XX99XX", "AB12CD", "AB-12-CD"], // 4
    ["XXXX99", "ABCD12", "AB-CD-12"], // 5
    ["99XXXX", "12ABCD", "12-AB-CD"], // 6
    ["99XXX9", "12ABC3", "12-ABC-3"], // 7
    ["9XXX99", "1ABC23", "1-ABC-23"], // 8
    ["XX999X", "AB123C", "AB-123-C"], // 9
    ["X999XX", "A123BC", "A-123-BC"], // 10
    ["XXX99X", "ABC12D", "ABC-12-D"], // 11
    ["X99XXX", "A12BCD", "A-12-BCD"], // 12
    ["9XX999", "1AB234", "1-AB-234"], // 13
    ["999XX9", "123AB4", "123-AB-4"], // 14
  ])("sidecode %s: %s → %s", (_sidecode, stored, shown) => {
    expect(formatLicencePlate(stored, "NL")).toBe(shown);
  });

  it("formats a typed plate, a lower-case country code and an already formatted plate the same way", () => {
    expect(formatLicencePlate("12-abc-3", "NL")).toBe("12-ABC-3");
    expect(formatLicencePlate("12abc3", "nl")).toBe("12-ABC-3");
    expect(formatLicencePlate("12-ABC-3", "NL")).toBe("12-ABC-3");
  });

  it("shows a Dutch plate that fits no sidecode, and every other country, as stored", () => {
    expect(formatLicencePlate("ABCDEFG", "NL")).toBe("ABCDEFG");
    expect(formatLicencePlate("123456", "NL")).toBe("123456");
    expect(formatLicencePlate("1-abc-123", "BE")).toBe("1ABC123");
    expect(formatLicencePlate("12ABC3", "BE")).toBe("12ABC3");
    expect(formatLicencePlate("12ABC3", null)).toBe("12ABC3");
    expect(formatLicencePlate(null, "NL")).toBe("");
    expect(formatLicencePlate("", "NL")).toBe("");
  });
});

describe("LicencePlateCell", () => {
  it("shows the row's plate in the display form of its registration country", () => {
    const { container, rerender } = render(<LicencePlateCell value="12ABC3" data={{ registrationCountryCode: "NL" }} />);
    expect(container).toHaveTextContent("12-ABC-3");

    /* The list DTO already carries the server-formatted plate; the cell must keep its dashes. */
    rerender(<LicencePlateCell value="12-ABC-3" data={{ registrationCountryCode: "NL" }} />);
    expect(container).toHaveTextContent("12-ABC-3");

    rerender(<LicencePlateCell value="1ABC123" data={{ registrationCountryCode: "BE" }} />);
    expect(container).toHaveTextContent("1ABC123");
    rerender(<LicencePlateCell value={null} data={undefined} />);
    expect(container).toHaveTextContent("");
  });
});
