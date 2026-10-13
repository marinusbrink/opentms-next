import { useState } from "react";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

/* The inline address of the relation editor (design 43 as revised by the PO on 2026-10-13 after the
 * old screen): the pure rules (an untouched address is no address; once anything was entered country,
 * street, postal code and city are required and the coordinates must be in range) and the component
 * (the old screen's field order and labels, control ids `${idPrefix}-${field}`, the required markers
 * only once something was entered, an error per field after its blur or once the editor was
 * submitted, the country pick handed to the caller for the time-zone follow, read-only).
 * Risk class: Medium (UI flow) for the component, Low for the pure helpers — unit and component tests
 * per the matrix. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));
vi.mock("@/components/ui/reference-combobox", async () => {
  const mocks = await import("./run2-test-mocks");
  return { ReferenceCombobox: mocks.MockReferenceCombobox };
});

import { AddressFields, hasAddressInput, newAddressRow, validateAddressRow, type AddressFieldsProps, type AddressRow } from "@/apps/master-data/components/AddressFields";

const complete = (): AddressRow => ({ ...newAddressRow("type-1"), countryId: "c-1", street: "Main street", houseNumber: "1", houseNumberSuffix: "a", addressLine2: "Building B", postalCode: "2611 AA", city: "Delft", latitude: "52.01", longitude: "4.36", timeZoneId: "Europe/Amsterdam" });

const TEXT_FIELDS = ["street", "houseNumber", "houseNumberSuffix", "addressLine2", "postalCode", "city", "latitude", "longitude"] as const;

describe("newAddressRow", () => {
  it("starts empty with the given address type, no id and a fresh key each time", () => {
    const a = newAddressRow();
    const b = newAddressRow("type-2");
    expect(a).toMatchObject({ id: null, addressTypeId: null, countryId: null, street: "", houseNumber: "", houseNumberSuffix: "", addressLine2: "", postalCode: "", city: "", latitude: "", longitude: "", timeZoneId: null });
    expect(b.addressTypeId).toBe("type-2");
    expect(a.key).not.toBe(b.key);
  });
});

describe("hasAddressInput", () => {
  it("an untouched address is no address; whitespace does not count", () => {
    expect(hasAddressInput(newAddressRow())).toBe(false);
    expect(hasAddressInput({ ...newAddressRow(), street: "   ", city: "\t", latitude: " " })).toBe(false);
  });

  it("any text field counts as input: street, number, suffix, building, postal code, city, latitude or longitude", () => {
    for (const field of TEXT_FIELDS) {
      expect(hasAddressInput({ ...newAddressRow(), [field]: "x" }), field).toBe(true);
    }
  });

  it("the country does not count by itself (PO 2026-10-13: a new address starts with the tenant's default country), nor does the time zone, nor both together", () => {
    expect(hasAddressInput({ ...newAddressRow(), countryId: "c-1" })).toBe(false);
    expect(hasAddressInput({ ...newAddressRow(), timeZoneId: "Europe/Amsterdam" })).toBe(false);
    expect(hasAddressInput({ ...newAddressRow(), countryId: "c-1", timeZoneId: "Europe/Amsterdam" })).toBe(false);
    /* With the country and zone prefilled, the first typed character makes it an address. */
    expect(hasAddressInput({ ...newAddressRow(), countryId: "c-1", timeZoneId: "Europe/Amsterdam", city: "D" })).toBe(true);
  });
});

describe("validateAddressRow", () => {
  it("has nothing to say about an untouched address, prefilled country and time zone included", () => {
    expect(validateAddressRow(newAddressRow())).toEqual({});
    expect(validateAddressRow({ ...newAddressRow(), countryId: "c-1", timeZoneId: "Europe/Amsterdam" })).toEqual({});
  });

  it("once something was entered next to the prefilled country, the country is satisfied and the other three are required", () => {
    expect(validateAddressRow({ ...newAddressRow(), countryId: "c-1", timeZoneId: "Europe/Amsterdam", houseNumber: "1" })).toEqual({
      street: "MasterData:StreetRequired",
      postalCode: "MasterData:PostalCodeRequired",
      city: "MasterData:CityRequired",
    });
  });

  it("once something was entered, country, street, postal code and city are required", () => {
    expect(validateAddressRow({ ...newAddressRow(), city: "Delft" })).toEqual({
      countryId: "MasterData:CountryRequired",
      street: "MasterData:StreetRequired",
      postalCode: "MasterData:PostalCodeRequired",
    });
    expect(validateAddressRow({ ...newAddressRow(), houseNumberSuffix: "a" })).toEqual({
      countryId: "MasterData:CountryRequired",
      street: "MasterData:StreetRequired",
      postalCode: "MasterData:PostalCodeRequired",
      city: "MasterData:CityRequired",
    });
    /* Whitespace in a required field is missing. */
    expect(validateAddressRow({ ...complete(), street: "  " })).toEqual({ street: "MasterData:StreetRequired" });
  });

  it("validates the coordinates: numbers within ±90 / ±180, a decimal comma allowed, empty allowed", () => {
    expect(validateAddressRow(complete())).toEqual({});
    expect(validateAddressRow({ ...complete(), latitude: "52,01", longitude: "4,36" })).toEqual({});
    expect(validateAddressRow({ ...complete(), latitude: "", longitude: "" })).toEqual({});
    expect(validateAddressRow({ ...complete(), latitude: "95" })).toEqual({ latitude: "MasterData:InvalidNumber" });
    expect(validateAddressRow({ ...complete(), latitude: "-90.5" })).toEqual({ latitude: "MasterData:InvalidNumber" });
    expect(validateAddressRow({ ...complete(), longitude: "-181" })).toEqual({ longitude: "MasterData:InvalidNumber" });
    expect(validateAddressRow({ ...complete(), longitude: "180.0001" })).toEqual({ longitude: "MasterData:InvalidNumber" });
    expect(validateAddressRow({ ...complete(), latitude: "north" })).toEqual({ latitude: "MasterData:InvalidNumber" });
  });
});

/* The component is controlled: the harness owns the row like the editor's form state does. */
function Harness({ initial, onChange, onCountryPicked, ...props }: { initial?: AddressRow; onChange?: (row: AddressRow) => void; onCountryPicked?: (id: string) => void } & Partial<Omit<AddressFieldsProps, "row" | "onChange" | "onCountryPicked">>) {
  const [row, setRow] = useState<AddressRow>(initial ?? newAddressRow());
  return (
    <AddressFields
      idPrefix="addr"
      row={row}
      onChange={(next) => {
        setRow(next);
        onChange?.(next);
      }}
      readOnly={false}
      showErrors={false}
      countryOptions={[
        { value: "c-1", label: "NL" },
        { value: "c-2", label: "DE" },
      ]}
      zoneOptions={[
        { value: "Europe/Amsterdam", label: "Europe/Amsterdam" },
        { value: "Europe/Berlin", label: "Europe/Berlin" },
      ]}
      onCountryPicked={onCountryPicked ?? (() => {})}
      {...props}
    />
  );
}

const CONTROLS = "input, select";
const escape = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
const control = (label: string) => screen.getByLabelText(new RegExp(`^${escape(label)}`), { selector: CONTROLS }) as HTMLInputElement;
const labelOf = (label: string) => screen.getByText(new RegExp(`^${escape(label)}`), { selector: "label" }) as HTMLLabelElement;
const block = () => document.querySelector<HTMLElement>("[data-slot=address-fields]")!;
const labels = () => Array.from(block().querySelectorAll<HTMLLabelElement>("label")).map((el) => el.textContent?.replace(/\s*\*\s*$/, "").trim());
const errors = () => Array.from(block().querySelectorAll<HTMLElement>("[data-slot=field-error]")).map((el) => el.textContent?.trim());

const LABELS = ["MasterData:Country", "MasterData:PostalCode", "MasterData:City", "MasterData:TimeZone", "MasterData:Street", "MasterData:Nr", "MasterData:HouseNumberSuffix", "MasterData:Building", "MasterData:Longitude", "MasterData:Latitude"];
const REQUIRED = ["MasterData:Country", "MasterData:PostalCode", "MasterData:City", "MasterData:Street"];

describe("AddressFields — layout", () => {
  it("renders the old screen's rows and labels in order, with the control ids under the prefix", () => {
    render(<Harness idPrefix="rel-defaultAddress" />);
    expect(labels()).toEqual(LABELS);
    expect(control("MasterData:Country").id).toBe("rel-defaultAddress-countryId");
    expect(control("MasterData:Country").tagName).toBe("SELECT");
    expect(control("MasterData:PostalCode").id).toBe("rel-defaultAddress-postalCode");
    expect(control("MasterData:City").id).toBe("rel-defaultAddress-city");
    expect(control("MasterData:TimeZone").id).toBe("rel-defaultAddress-timeZoneId");
    expect(control("MasterData:TimeZone").tagName).toBe("SELECT");
    expect(control("MasterData:Street").id).toBe("rel-defaultAddress-street");
    expect(control("MasterData:Nr").id).toBe("rel-defaultAddress-houseNumber");
    expect(control("MasterData:HouseNumberSuffix").id).toBe("rel-defaultAddress-houseNumberSuffix");
    expect(control("MasterData:Building").id).toBe("rel-defaultAddress-addressLine2");
    expect(control("MasterData:Longitude").id).toBe("rel-defaultAddress-longitude");
    expect(control("MasterData:Latitude").id).toBe("rel-defaultAddress-latitude");
    /* Lengths and the numeric keyboard for the coordinates. */
    expect(control("MasterData:PostalCode").maxLength).toBe(16);
    expect(control("MasterData:City").maxLength).toBe(128);
    expect(control("MasterData:Street").maxLength).toBe(128);
    expect(control("MasterData:Nr").maxLength).toBe(16);
    expect(control("MasterData:HouseNumberSuffix").maxLength).toBe(16);
    expect(control("MasterData:Building").maxLength).toBe(128);
    expect(control("MasterData:Latitude").maxLength).toBe(20);
    expect(control("MasterData:Latitude")).toHaveAttribute("inputmode", "decimal");
    expect(control("MasterData:Longitude")).toHaveAttribute("inputmode", "decimal");
    expect(control("MasterData:Street")).not.toHaveAttribute("inputmode");
    /* The country options carry the code only. */
    expect(Array.from((control("MasterData:Country") as unknown as HTMLSelectElement).options).map((o) => o.textContent)).toEqual(["—", "NL", "DE"]);
  });

  it("shows the loaded row in its controls", () => {
    render(<Harness initial={complete()} />);
    expect(control("MasterData:Country").value).toBe("c-1");
    expect(control("MasterData:PostalCode").value).toBe("2611 AA");
    expect(control("MasterData:City").value).toBe("Delft");
    expect(control("MasterData:TimeZone").value).toBe("Europe/Amsterdam");
    expect(control("MasterData:Street").value).toBe("Main street");
    expect(control("MasterData:Nr").value).toBe("1");
    expect(control("MasterData:HouseNumberSuffix").value).toBe("a");
    expect(control("MasterData:Building").value).toBe("Building B");
    expect(control("MasterData:Longitude").value).toBe("4.36");
    expect(control("MasterData:Latitude").value).toBe("52.01");
  });

  it("read-only disables every control", () => {
    render(<Harness initial={complete()} readOnly />);
    for (const label of LABELS) {
      expect(control(label), label).toBeDisabled();
    }
  });
});

describe("AddressFields — required and errors", () => {
  it("carries no required marker while the address is untouched; once anything is entered country, street, postal code and city are required", () => {
    render(<Harness />);
    for (const label of LABELS) {
      expect(labelOf(label), label).not.toHaveTextContent("*");
      expect(control(label), label).not.toHaveAttribute("aria-required");
    }
    fireEvent.change(control("MasterData:HouseNumberSuffix"), { target: { value: "a" } });
    for (const label of LABELS) {
      if (REQUIRED.includes(label)) {
        expect(labelOf(label), label).toHaveTextContent("*");
        expect(control(label), label).toHaveAttribute("aria-required", "true");
      } else {
        expect(labelOf(label), label).not.toHaveTextContent("*");
        expect(control(label), label).not.toHaveAttribute("aria-required");
      }
    }
    /* Nothing is flagged yet: no field was blurred and the editor was not submitted. */
    expect(errors()).toEqual([]);
    /* Clearing the input again drops the markers. */
    fireEvent.change(control("MasterData:HouseNumberSuffix"), { target: { value: "" } });
    expect(labelOf("MasterData:Country")).not.toHaveTextContent("*");
  });

  it("a row that starts with the country and its time zone prefilled is still untouched: no markers, no errors on submit, until a text field is entered", () => {
    const prefilled = { ...newAddressRow(), countryId: "c-1", timeZoneId: "Europe/Amsterdam" };
    const { rerender } = render(<Harness initial={prefilled} />);
    expect(control("MasterData:Country").value).toBe("c-1");
    expect(control("MasterData:TimeZone").value).toBe("Europe/Amsterdam");
    for (const label of LABELS) {
      expect(labelOf(label), label).not.toHaveTextContent("*");
      expect(control(label), label).not.toHaveAttribute("aria-required");
    }
    /* Submitted with nothing but the prefill: nothing is required, nothing flagged. */
    rerender(<Harness initial={prefilled} showErrors />);
    expect(errors()).toEqual([]);
    for (const label of LABELS) {
      expect(control(label), label).not.toHaveAttribute("aria-invalid");
    }
    /* Picking another country alone keeps it untouched too. */
    fireEvent.change(control("MasterData:Country"), { target: { value: "c-2" } });
    expect(labelOf("MasterData:Street")).not.toHaveTextContent("*");
    expect(errors()).toEqual([]);
    /* The first text entry makes it an address: the three text fields are required, the country already satisfied. */
    fireEvent.change(control("MasterData:City"), { target: { value: "Delft" } });
    expect(labelOf("MasterData:Country")).toHaveTextContent("*");
    expect(labelOf("MasterData:Street")).toHaveTextContent("*");
    expect(errors()).toEqual(["MasterData:PostalCodeRequired", "MasterData:StreetRequired"]);
    expect(control("MasterData:Country")).not.toHaveAttribute("aria-invalid");
  });

  it("shows a field's error after its own blur only, bound to the input through aria-describedby", () => {
    render(<Harness />);
    fireEvent.change(control("MasterData:City"), { target: { value: "Delft" } });
    fireEvent.blur(control("MasterData:City"));
    /* The city is filled: its blur shows nothing. */
    expect(errors()).toEqual([]);

    fireEvent.blur(control("MasterData:Street"));
    expect(errors()).toEqual(["MasterData:StreetRequired"]);
    expect(control("MasterData:Street")).toHaveAttribute("aria-invalid", "true");
    expect(control("MasterData:Street")).toHaveAttribute("aria-describedby", "addr-street-error");
    expect(document.getElementById("addr-street-error")).toHaveTextContent("MasterData:StreetRequired");
    expect(control("MasterData:PostalCode")).not.toHaveAttribute("aria-invalid");
    expect(control("MasterData:Country")).not.toHaveAttribute("aria-invalid");

    fireEvent.blur(control("MasterData:PostalCode"));
    expect(errors()).toEqual(["MasterData:PostalCodeRequired", "MasterData:StreetRequired"]);

    /* Filling the field clears its error; the touched state stays, so emptying it again flags it at once. */
    fireEvent.change(control("MasterData:Street"), { target: { value: "Main street" } });
    expect(errors()).toEqual(["MasterData:PostalCodeRequired"]);
    fireEvent.change(control("MasterData:Street"), { target: { value: "" } });
    expect(errors()).toEqual(["MasterData:PostalCodeRequired", "MasterData:StreetRequired"]);
  });

  it("flags an out-of-range coordinate after blur and clears it when corrected", () => {
    render(<Harness initial={complete()} />);
    fireEvent.change(control("MasterData:Latitude"), { target: { value: "95" } });
    expect(errors()).toEqual([]);
    fireEvent.blur(control("MasterData:Latitude"));
    expect(errors()).toEqual(["MasterData:InvalidNumber"]);
    expect(control("MasterData:Latitude")).toHaveAttribute("aria-invalid", "true");
    fireEvent.change(control("MasterData:Latitude"), { target: { value: "52,01" } });
    expect(errors()).toEqual([]);
  });

  it("showErrors (the editor was submitted) shows every error without a blur", () => {
    const { rerender } = render(<Harness initial={{ ...newAddressRow(), city: "Delft", longitude: "-181" }} />);
    expect(errors()).toEqual([]);
    rerender(<Harness initial={{ ...newAddressRow(), city: "Delft", longitude: "-181" }} showErrors />);
    expect(errors()).toEqual(["MasterData:CountryRequired", "MasterData:PostalCodeRequired", "MasterData:StreetRequired", "MasterData:InvalidNumber"]);
    expect(control("MasterData:City")).not.toHaveAttribute("aria-invalid");
    expect(control("MasterData:Longitude")).toHaveAttribute("aria-invalid", "true");
  });

  it("an untouched address shows no error even when the editor was submitted", () => {
    render(<Harness showErrors />);
    expect(errors()).toEqual([]);
    for (const label of LABELS) {
      expect(control(label), label).not.toHaveAttribute("aria-invalid");
    }
  });
});

describe("AddressFields — changes", () => {
  it("hands every edit to the caller as a new row, keeping the key, id and address type", () => {
    const onChange = vi.fn<(row: AddressRow) => void>();
    const initial = { ...newAddressRow("type-2"), key: "row-x", id: "a9" };
    render(<Harness initial={initial} onChange={onChange} />);
    fireEvent.change(control("MasterData:Street"), { target: { value: "Main street" } });
    fireEvent.change(control("MasterData:Nr"), { target: { value: "1" } });
    fireEvent.change(control("MasterData:HouseNumberSuffix"), { target: { value: "a" } });
    fireEvent.change(control("MasterData:Building"), { target: { value: "Building B" } });
    fireEvent.change(control("MasterData:TimeZone"), { target: { value: "Europe/Berlin" } });
    expect(onChange).toHaveBeenCalledTimes(5);
    expect(onChange.mock.calls[4][0]).toEqual({ ...initial, street: "Main street", houseNumber: "1", houseNumberSuffix: "a", addressLine2: "Building B", timeZoneId: "Europe/Berlin" });
    expect(control("MasterData:HouseNumberSuffix").value).toBe("a");
  });

  it("a country pick updates the row, marks the country touched and tells the caller; clearing it does not", () => {
    const onChange = vi.fn<(row: AddressRow) => void>();
    const onCountryPicked = vi.fn<(id: string) => void>();
    render(<Harness onChange={onChange} onCountryPicked={onCountryPicked} />);
    fireEvent.change(control("MasterData:Country"), { target: { value: "c-2" } });
    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ countryId: "c-2" }));
    expect(onCountryPicked).toHaveBeenCalledWith("c-2");
    expect(control("MasterData:Country").value).toBe("c-2");

    /* The country was touched: clearing it while the street keeps the address alive flags it at once. */
    fireEvent.change(control("MasterData:Street"), { target: { value: "Main street" } });
    fireEvent.change(control("MasterData:Country"), { target: { value: "" } });
    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ countryId: null, street: "Main street" }));
    expect(onCountryPicked).toHaveBeenCalledTimes(1);
    expect(within(block()).getByText("MasterData:CountryRequired")).toBeInTheDocument();
  });
});
