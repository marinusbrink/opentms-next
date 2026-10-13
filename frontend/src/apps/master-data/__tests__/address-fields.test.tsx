import { useState } from "react";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

/* The inline address of the relation editor (design 43 as revised by the PO on 2026-10-13 after the
 * old screen): the pure rules (an untouched address is no address; once anything was entered country,
 * street, postal code and city are required and the coordinates must be in range) and the component
 * (the old screen's field order and labels, control ids `${idPrefix}-${field}`, the required markers
 * only once something was entered, an error per field after its blur or once the editor was
 * submitted, the country pick handed to the caller for the time-zone follow, read-only).
 * Risk class: Medium (UI flow) for the component, Low for the pure helpers — unit and component tests
 * per the matrix. Design 44 (the postal-code lookup: applyLookup's fill-only-empty rule and the blur
 * trigger) is covered at the end of this file, followed by the postal-code suggestions dropdown (PO
 * 2026-10-13, design 44 "Suggestions" / "UI": applySuggestion's user's-pick rule, the Base UI combobox
 * with its 250 ms debounce, the three-character minimum, the stale-answer rule and the pick). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));
vi.mock("@/components/ui/reference-combobox", async () => {
  const mocks = await import("./run2-test-mocks");
  return { ReferenceCombobox: mocks.MockReferenceCombobox };
});

import { AddressFields, applyLookup, applySuggestion, hasAddressInput, newAddressRow, validateAddressRow, type AddressFieldsProps, type AddressLookupFill, type AddressRow, type AddressSuggestionLine } from "@/apps/master-data/components/AddressFields";

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

/* ── Design 44: the postal-code lookup ───────────────────────────────────────────────────────────
 * applyLookup (Low: a pure rule) fills only the empty fields among street, house number, city and the
 * coordinates; the component (Medium: UI flow) asks the lookup when the postal code or the house number
 * loses focus while a country and a postal code are set, marks the block busy meanwhile, and treats a
 * null answer or a failure as nothing to fill. Only a fake onLookup: the editor's own gate (feature,
 * permission, the country's code, the HTTP call) is covered in relation-editor.test.tsx. */

const FILL: AddressLookupFill = { street: "Dam", houseNumber: "1", city: "Amsterdam", latitude: 52.3731, longitude: 4.8932 };
const FILLED_TEXT = { street: "Dam", houseNumber: "1", city: "Amsterdam", latitude: "52.3731", longitude: "4.8932" };

describe("applyLookup (design 44)", () => {
  it("fills the empty street, house number, city and coordinates; the numbers become the row's text; everything else stays", () => {
    const row: AddressRow = { ...newAddressRow("type-1"), key: "row-x", id: "a9", countryId: "c-1", postalCode: "1012AB", houseNumberSuffix: "a", addressLine2: "Unit 3", timeZoneId: "Europe/Amsterdam" };
    expect(applyLookup(row, FILL)).toEqual({ ...row, ...FILLED_TEXT });
  });

  it("keeps what the user typed, field by field: a typed city and latitude stay while the street and longitude are filled", () => {
    const row: AddressRow = { ...newAddressRow(), countryId: "c-1", postalCode: "1012AB", city: "Delft", latitude: "52.01" };
    expect(applyLookup(row, FILL)).toEqual({ ...row, street: "Dam", houseNumber: "1", city: "Delft", latitude: "52.01", longitude: "4.8932" });
  });

  it("changes nothing on a complete address", () => {
    const row = complete();
    expect(applyLookup(row, FILL)).toEqual(row);
  });

  it("a whitespace-only field counts as empty (like hasAddressInput) and is filled", () => {
    const row: AddressRow = { ...newAddressRow(), city: "   ", street: "\t", latitude: " " };
    expect(applyLookup(row, FILL)).toMatchObject({ city: "Amsterdam", street: "Dam", latitude: "52.3731" });
  });

  it("an absent, null or empty answer leaves the field as it is; a zero coordinate is an answer", () => {
    const row = newAddressRow();
    expect(applyLookup(row, {})).toEqual(row);
    expect(applyLookup(row, { street: null, houseNumber: "", city: undefined, latitude: null, longitude: undefined })).toEqual(row);
    expect(applyLookup(row, { latitude: 0, longitude: 0 })).toMatchObject({ latitude: "0", longitude: "0" });
  });

  it("never fills the postal code, the suffix, the building, the country or the time zone", () => {
    const row = newAddressRow();
    const result = applyLookup(row, FILL);
    expect(result).toMatchObject({ postalCode: "", houseNumberSuffix: "", addressLine2: "", countryId: null, timeZoneId: null, key: row.key, id: null });
  });

  it("returns a new row and leaves its input untouched", () => {
    const row = newAddressRow();
    const result = applyLookup(row, FILL);
    expect(result).not.toBe(row);
    expect(row.street).toBe("");
  });
});

type Lookup = (row: AddressRow) => Promise<AddressLookupFill | null>;

interface Deferred {
  promise: Promise<AddressLookupFill | null>;
  resolve: (value: AddressLookupFill | null) => void;
  reject: (reason: unknown) => void;
}
const deferred = (): Deferred => {
  let resolve!: Deferred["resolve"];
  let reject!: Deferred["reject"];
  const promise = new Promise<AddressLookupFill | null>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
};

/** A row the lookup applies to: a country and a postal code, nothing else. */
const lookupReady = (): AddressRow => ({ ...newAddressRow(), key: "row-l", countryId: "c-1", postalCode: "1012AB", timeZoneId: "Europe/Amsterdam" });
const values = () => ({
  street: control("MasterData:Street").value,
  houseNumber: control("MasterData:Nr").value,
  city: control("MasterData:City").value,
  latitude: control("MasterData:Latitude").value,
  longitude: control("MasterData:Longitude").value,
});
const EMPTY_VALUES = { street: "", houseNumber: "", city: "", latitude: "", longitude: "" };
/* Lets the component's pending promise settle inside act. */
const settle = () => act(async () => { await Promise.resolve(); });

describe("AddressFields — postal-code lookup (design 44)", () => {
  it("asks the lookup with the row as it is when the postal code loses focus, marks the block busy meanwhile and fills the empty fields with the awaited answer", async () => {
    const d = deferred();
    const onLookup = vi.fn<Lookup>().mockReturnValue(d.promise);
    const onChange = vi.fn<(row: AddressRow) => void>();
    const initial: AddressRow = { ...lookupReady(), postalCode: "" };
    render(<Harness initial={initial} onChange={onChange} onLookup={onLookup} />);
    expect(block()).not.toHaveAttribute("aria-busy");

    fireEvent.change(control("MasterData:PostalCode"), { target: { value: "1012AB" } });
    expect(onLookup).not.toHaveBeenCalled();
    fireEvent.blur(control("MasterData:PostalCode"));
    expect(onLookup).toHaveBeenCalledTimes(1);
    expect(onLookup).toHaveBeenCalledWith({ ...initial, postalCode: "1012AB" });
    expect(block()).toHaveAttribute("aria-busy", "true");
    /* The typed postal code reached the caller; nothing else yet. */
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(values()).toEqual(EMPTY_VALUES);

    await act(async () => {
      d.resolve(FILL);
      await d.promise;
    });
    expect(block()).not.toHaveAttribute("aria-busy");
    expect(onChange).toHaveBeenCalledTimes(2);
    expect(onChange).toHaveBeenLastCalledWith({ ...initial, postalCode: "1012AB", ...FILLED_TEXT });
    expect(values()).toEqual(FILLED_TEXT);
    expect(control("MasterData:PostalCode").value).toBe("1012AB");
    expect(control("MasterData:Country").value).toBe("c-1");
    expect(control("MasterData:TimeZone").value).toBe("Europe/Amsterdam");
  });

  it("the house number's blur asks too; the blur of any other field does not", async () => {
    const onLookup = vi.fn<Lookup>().mockResolvedValue(null);
    render(<Harness initial={lookupReady()} onLookup={onLookup} />);
    fireEvent.blur(control("MasterData:Nr"));
    expect(onLookup).toHaveBeenCalledTimes(1);
    await settle();
    for (const label of ["MasterData:City", "MasterData:Street", "MasterData:HouseNumberSuffix", "MasterData:Building", "MasterData:Longitude", "MasterData:Latitude"]) {
      fireEvent.blur(control(label));
    }
    await settle();
    expect(onLookup).toHaveBeenCalledTimes(1);
    /* Each blur asks again: a second postal-code blur is a second lookup. */
    fireEvent.blur(control("MasterData:PostalCode"));
    expect(onLookup).toHaveBeenCalledTimes(2);
    await settle();
  });

  it("keeps the typed city and latitude and fills only the rest; the coordinates arrive as text", async () => {
    const onLookup = vi.fn<Lookup>().mockResolvedValue(FILL);
    const onChange = vi.fn<(row: AddressRow) => void>();
    render(<Harness initial={{ ...lookupReady(), city: "Delft", latitude: "52.01" }} onChange={onChange} onLookup={onLookup} />);
    fireEvent.blur(control("MasterData:PostalCode"));
    await waitFor(() => expect(control("MasterData:Street").value).toBe("Dam"));
    expect(values()).toEqual({ street: "Dam", houseNumber: "1", city: "Delft", latitude: "52.01", longitude: "4.8932" });
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenLastCalledWith({ ...lookupReady(), city: "Delft", latitude: "52.01", street: "Dam", houseNumber: "1", longitude: "4.8932" });
  });

  it("a city typed while the lookup is still running stays: the answer fills what is empty when it arrives, not when it was asked", async () => {
    const d = deferred();
    const onLookup = vi.fn<Lookup>().mockReturnValue(d.promise);
    render(<Harness initial={lookupReady()} onLookup={onLookup} />);
    fireEvent.blur(control("MasterData:PostalCode"));
    fireEvent.change(control("MasterData:City"), { target: { value: "Delft" } });
    expect(block()).toHaveAttribute("aria-busy", "true");
    await act(async () => {
      d.resolve(FILL);
      await d.promise;
    });
    expect(values()).toEqual({ ...FILLED_TEXT, city: "Delft" });
    expect(block()).not.toHaveAttribute("aria-busy");
  });

  it("does not ask without a country, nor without a postal code (whitespace is none)", async () => {
    const onLookup = vi.fn<Lookup>().mockResolvedValue(FILL);
    const first = render(<Harness initial={{ ...lookupReady(), countryId: null }} onLookup={onLookup} />);
    fireEvent.blur(control("MasterData:PostalCode"));
    fireEvent.blur(control("MasterData:Nr"));
    await settle();
    expect(onLookup).not.toHaveBeenCalled();
    expect(block()).not.toHaveAttribute("aria-busy");
    expect(values()).toEqual(EMPTY_VALUES);
    first.unmount();

    render(<Harness initial={{ ...lookupReady(), postalCode: "   " }} onLookup={onLookup} />);
    fireEvent.blur(control("MasterData:PostalCode"));
    fireEvent.blur(control("MasterData:Nr"));
    await settle();
    expect(onLookup).not.toHaveBeenCalled();
    expect(block()).not.toHaveAttribute("aria-busy");
    expect(values()).toEqual(EMPTY_VALUES);
  });

  it("read-only never asks", async () => {
    const onLookup = vi.fn<Lookup>().mockResolvedValue(FILL);
    const onChange = vi.fn<(row: AddressRow) => void>();
    render(<Harness initial={lookupReady()} onChange={onChange} onLookup={onLookup} readOnly />);
    fireEvent.blur(control("MasterData:PostalCode"));
    fireEvent.blur(control("MasterData:Nr"));
    await settle();
    expect(onLookup).not.toHaveBeenCalled();
    expect(onChange).not.toHaveBeenCalled();
    expect(block()).not.toHaveAttribute("aria-busy");
  });

  it("without onLookup (the feature is off) a blur is only a blur: no busy state, no change", async () => {
    const onChange = vi.fn<(row: AddressRow) => void>();
    render(<Harness initial={lookupReady()} onChange={onChange} />);
    fireEvent.blur(control("MasterData:PostalCode"));
    await settle();
    expect(block()).not.toHaveAttribute("aria-busy");
    expect(onChange).not.toHaveBeenCalled();
    expect(values()).toEqual(EMPTY_VALUES);
  });

  it("a rejected lookup leaves the row as it is, reports nothing and clears the busy state", async () => {
    const d = deferred();
    const onLookup = vi.fn<Lookup>().mockReturnValue(d.promise);
    const onChange = vi.fn<(row: AddressRow) => void>();
    render(<Harness initial={{ ...lookupReady(), city: "Delft" }} onChange={onChange} onLookup={onLookup} />);
    fireEvent.blur(control("MasterData:PostalCode"));
    expect(block()).toHaveAttribute("aria-busy", "true");
    await act(async () => {
      d.reject(new Error("provider down"));
      await d.promise.catch(() => undefined);
    });
    expect(block()).not.toHaveAttribute("aria-busy");
    expect(onChange).not.toHaveBeenCalled();
    expect(values()).toEqual({ ...EMPTY_VALUES, city: "Delft" });
    expect(errors()).toEqual([]);
  });

  it("a null answer (nothing found) leaves the row as it is and clears the busy state", async () => {
    const onLookup = vi.fn<Lookup>().mockResolvedValue(null);
    const onChange = vi.fn<(row: AddressRow) => void>();
    render(<Harness initial={{ ...lookupReady(), houseNumber: "7" }} onChange={onChange} onLookup={onLookup} />);
    fireEvent.blur(control("MasterData:Nr"));
    expect(onLookup).toHaveBeenCalledWith({ ...lookupReady(), houseNumber: "7" });
    await waitFor(() => expect(block()).not.toHaveAttribute("aria-busy"));
    expect(onChange).not.toHaveBeenCalled();
    expect(values()).toEqual({ ...EMPTY_VALUES, houseNumber: "7" });
  });
});

/* ------------------------------------------------------------------------------------------------
 * The postal-code suggestions dropdown (PO 2026-10-13, extension of design 44).
 * Risk class: Low for applySuggestion (pure helper), Medium (UI flow) for the combobox — unit and
 * component tests per the matrix. The feature/permission gate is the editor's and is covered in
 * relation-editor.test.tsx. */

/** A postal-code line: four digits and letters with their street, no number. */
const PC_LINE: AddressSuggestionLine = { label: "1012 AB Dam, Amsterdam", postalCode: "1012 AB", street: "Dam", city: "Amsterdam", latitude: 52.3731, longitude: 4.8932 };
/** An address line: a number with its suffix. */
const ADDRESS_LINE: AddressSuggestionLine = { label: "Dam 1A, 1012 AB Amsterdam", postalCode: "1012 AB", street: "Dam", houseNumber: "1", houseNumberSuffix: "A", city: "Amsterdam", latitude: 52.373, longitude: 4.893 };
/** An address line with a number and no suffix. */
const PLAIN_NUMBER_LINE: AddressSuggestionLine = { label: "Dam 3, 1012 AB Amsterdam", postalCode: "1012 AB", street: "Dam", houseNumber: "3", houseNumberSuffix: null, city: "Amsterdam", latitude: 52.3732, longitude: 4.8933 };

describe("applySuggestion (PO 2026-10-13)", () => {
  it("an address line sets postal code, street, city, number, suffix and the coordinates as text; everything else stays", () => {
    const row: AddressRow = { ...newAddressRow("type-1"), key: "row-x", id: "a9", countryId: "c-1", addressLine2: "Unit 3", timeZoneId: "Europe/Amsterdam" };
    expect(applySuggestion(row, ADDRESS_LINE)).toEqual({ ...row, postalCode: "1012 AB", street: "Dam", houseNumber: "1", houseNumberSuffix: "A", city: "Amsterdam", latitude: "52.373", longitude: "4.893" });
  });

  it("is the user's own pick: it overwrites what was typed, unlike the blur lookup", () => {
    const row: AddressRow = { ...complete(), postalCode: "1012ab" };
    expect(applySuggestion(row, ADDRESS_LINE)).toEqual({ ...row, postalCode: "1012 AB", street: "Dam", houseNumber: "1", houseNumberSuffix: "A", city: "Amsterdam", latitude: "52.373", longitude: "4.893" });
  });

  it("a line with a number and no suffix clears the row's suffix (null or absent alike)", () => {
    const row: AddressRow = { ...complete(), houseNumber: "12", houseNumberSuffix: "b" };
    expect(applySuggestion(row, PLAIN_NUMBER_LINE)).toMatchObject({ houseNumber: "3", houseNumberSuffix: "" });
    const { houseNumberSuffix: _omitted, ...withoutSuffix } = PLAIN_NUMBER_LINE;
    void _omitted;
    expect(applySuggestion(row, withoutSuffix)).toMatchObject({ houseNumber: "3", houseNumberSuffix: "" });
  });

  it("a postal-code line keeps the row's number and suffix and sets the rest", () => {
    const row: AddressRow = { ...newAddressRow(), countryId: "c-1", houseNumber: "7", houseNumberSuffix: "b", street: "Old street", city: "Delft", latitude: "52.01", longitude: "4.36" };
    expect(applySuggestion(row, PC_LINE)).toEqual({ ...row, postalCode: "1012 AB", street: "Dam", city: "Amsterdam", latitude: "52.3731", longitude: "4.8932" });
    expect(applySuggestion(row, { ...PC_LINE, houseNumber: null, houseNumberSuffix: "X" })).toMatchObject({ houseNumber: "7", houseNumberSuffix: "b" });
  });

  it("an absent or null street, city or coordinate keeps the row's value; a zero coordinate is a value", () => {
    const row: AddressRow = { ...complete() };
    expect(applySuggestion(row, { label: "1012", postalCode: "1012 AB" })).toEqual({ ...row, postalCode: "1012 AB" });
    expect(applySuggestion(row, { label: "1012", postalCode: "1012 AB", street: null, city: null, latitude: null, longitude: null })).toEqual({ ...row, postalCode: "1012 AB" });
    expect(applySuggestion(row, { label: "0", postalCode: "0", latitude: 0, longitude: 0 })).toMatchObject({ latitude: "0", longitude: "0" });
  });

  it("returns a new row and leaves its input untouched", () => {
    const row = newAddressRow();
    const result = applySuggestion(row, ADDRESS_LINE);
    expect(result).not.toBe(row);
    expect(row).toMatchObject({ postalCode: "", street: "", houseNumber: "", houseNumberSuffix: "", city: "", latitude: "", longitude: "" });
  });
});

type Suggest = (row: AddressRow, term: string) => Promise<AddressSuggestionLine[]>;

interface LinesDeferred {
  promise: Promise<AddressSuggestionLine[]>;
  resolve: (value: AddressSuggestionLine[]) => void;
}
const linesDeferred = (): LinesDeferred => {
  let resolve!: LinesDeferred["resolve"];
  const promise = new Promise<AddressSuggestionLine[]>((res) => {
    resolve = res;
  });
  return { promise, resolve };
};

/** A row the dropdown applies to: the country is set, nothing typed yet. */
const suggestReady = (): AddressRow => ({ ...newAddressRow(), key: "row-s", countryId: "c-1", timeZoneId: "Europe/Amsterdam" });
const postal = () => control("MasterData:PostalCode");
/* Base UI reads `inputType` to tell typing from autofill. */
const typePostal = (value: string, inputType = "insertText") => fireEvent.input(postal(), { target: { value }, inputType });
/* The debounce runs on the fake clock; the async variant also lets the answered promise settle. */
const advance = (ms: number) => act(async () => { await vi.advanceTimersByTimeAsync(ms); });
/* The dropdown's lines only, not the country and time-zone selects' options. */
const suggestionItems = () => Array.from(document.querySelectorAll<HTMLElement>("[data-slot=combobox-item][role=option]"));
const options = () => suggestionItems().map((el) => el.textContent?.trim());
const option = (label: string) => suggestionItems().find((el) => el.textContent?.trim() === label)!;

describe("AddressFields — postal-code suggestions (PO 2026-10-13)", () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout"] });
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it("with onSuggest the postal code is a combobox under the same id and label, autocomplete off, max 16; the field order is unchanged", () => {
    render(<Harness idPrefix="rel-defaultAddress" onSuggest={vi.fn<Suggest>().mockResolvedValue([])} />);
    expect(labels()).toEqual(LABELS);
    expect(postal()).toHaveAttribute("role", "combobox");
    expect(screen.getByRole("combobox", { name: /^MasterData:PostalCode/ })).toBe(postal());
    expect(postal().id).toBe("rel-defaultAddress-postalCode");
    expect(postal()).toHaveAttribute("autocomplete", "off");
    expect(postal().maxLength).toBe(16);
    expect(postal()).not.toBeDisabled();
    expect(screen.queryByRole("textbox", { name: /^MasterData:PostalCode/ })).toBeNull();
  });

  it("without onSuggest (the feature is off) the postal code stays a plain textbox", () => {
    render(<Harness />);
    expect(screen.getByRole("textbox", { name: /^MasterData:PostalCode/ })).toBe(postal());
    expect(screen.queryByRole("combobox", { name: /^MasterData:PostalCode/ })).toBeNull();
  });

  it("read-only keeps the plain disabled textbox and never asks, onSuggest notwithstanding", async () => {
    const onSuggest = vi.fn<Suggest>().mockResolvedValue([PC_LINE]);
    render(<Harness initial={{ ...suggestReady(), postalCode: "1012AB" }} onSuggest={onSuggest} readOnly />);
    expect(screen.getByRole("textbox", { name: /^MasterData:PostalCode/ })).toBe(postal());
    expect(postal()).toBeDisabled();
    expect(postal().value).toBe("1012AB");
    expect(screen.queryByRole("combobox", { name: /^MasterData:PostalCode/ })).toBeNull();
    await advance(1000);
    expect(onSuggest).not.toHaveBeenCalled();
  });

  it("typing updates the postal code on every keystroke and asks once, 250 ms after the last keystroke, with the row as it is and the term", async () => {
    const onSuggest = vi.fn<Suggest>().mockResolvedValue([]);
    const onChange = vi.fn<(row: AddressRow) => void>();
    render(<Harness initial={suggestReady()} onChange={onChange} onSuggest={onSuggest} />);
    typePostal("1");
    typePostal("10");
    typePostal("101");
    await advance(200);
    typePostal("1012");
    expect(onChange).toHaveBeenCalledTimes(4);
    expect(onChange).toHaveBeenLastCalledWith({ ...suggestReady(), postalCode: "1012" });
    expect(postal().value).toBe("1012");
    await advance(249);
    expect(onSuggest).not.toHaveBeenCalled();
    await advance(1);
    expect(onSuggest).toHaveBeenCalledTimes(1);
    expect(onSuggest).toHaveBeenCalledWith({ ...suggestReady(), postalCode: "1012" }, "1012");
    await advance(1000);
    expect(onSuggest).toHaveBeenCalledTimes(1);
  });

  it("a paste counts as typing", async () => {
    const onSuggest = vi.fn<Suggest>().mockResolvedValue([]);
    render(<Harness initial={suggestReady()} onSuggest={onSuggest} />);
    typePostal("1012 AB", "insertFromPaste");
    expect(postal().value).toBe("1012 AB");
    await advance(250);
    expect(onSuggest).toHaveBeenCalledWith({ ...suggestReady(), postalCode: "1012 AB" }, "1012 AB");
  });

  it("asks only from three non-space characters on; spaces do not count", async () => {
    const onSuggest = vi.fn<Suggest>().mockResolvedValue([]);
    render(<Harness initial={suggestReady()} onSuggest={onSuggest} />);
    typePostal("10");
    await advance(1000);
    typePostal("1 0 ");
    await advance(1000);
    typePostal("   ");
    await advance(1000);
    expect(onSuggest).not.toHaveBeenCalled();
    typePostal("10 1");
    await advance(250);
    expect(onSuggest).toHaveBeenCalledTimes(1);
    expect(onSuggest).toHaveBeenCalledWith(expect.objectContaining({ postalCode: "10 1" }), "10 1");
  });

  it("does not ask without a country; once a country is picked the next keystroke asks", async () => {
    const onSuggest = vi.fn<Suggest>().mockResolvedValue([]);
    render(<Harness initial={{ ...suggestReady(), countryId: null }} onSuggest={onSuggest} />);
    typePostal("1012");
    await advance(1000);
    expect(onSuggest).not.toHaveBeenCalled();
    expect(postal().value).toBe("1012");
    fireEvent.change(control("MasterData:Country"), { target: { value: "c-1" } });
    typePostal("1012A");
    await advance(250);
    expect(onSuggest).toHaveBeenCalledTimes(1);
    expect(onSuggest).toHaveBeenCalledWith(expect.objectContaining({ countryId: "c-1", postalCode: "1012A" }), "1012A");
  });

  it("the popup opens only when lines come back, one option per line by its label", async () => {
    const onSuggest = vi.fn<Suggest>().mockResolvedValueOnce([]).mockResolvedValueOnce([PC_LINE, ADDRESS_LINE]);
    render(<Harness initial={suggestReady()} onSuggest={onSuggest} />);
    typePostal("9999");
    await advance(250);
    expect(onSuggest).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("listbox")).toBeNull();
    expect(options()).toEqual([]);
    expect(postal()).toHaveAttribute("aria-expanded", "false");

    typePostal("1012");
    await advance(250);
    expect(onSuggest).toHaveBeenCalledTimes(2);
    expect(options()).toEqual([PC_LINE.label, ADDRESS_LINE.label]);
    expect(postal()).toHaveAttribute("aria-expanded", "true");
    /* Typing stays free: nothing was picked, the postal code is as typed. */
    expect(postal().value).toBe("1012");
  });

  it("a failing suggestion request is silent: no popup, no error, the typed postal code stays", async () => {
    const onSuggest = vi.fn<Suggest>().mockRejectedValue(new Error("provider down"));
    const onChange = vi.fn<(row: AddressRow) => void>();
    render(<Harness initial={suggestReady()} onChange={onChange} onSuggest={onSuggest} />);
    typePostal("1012");
    await advance(250);
    expect(onSuggest).toHaveBeenCalledTimes(1);
    expect(options()).toEqual([]);
    expect(errors()).toEqual([]);
    expect(postal().value).toBe("1012");
    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it("an answer for an older term is dropped when the newer one answered first", async () => {
    const first = linesDeferred();
    const second = linesDeferred();
    const onSuggest = vi.fn<Suggest>().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);
    render(<Harness initial={suggestReady()} onSuggest={onSuggest} />);
    typePostal("1012");
    await advance(250);
    typePostal("1012AB");
    await advance(250);
    expect(onSuggest.mock.calls.map(([, term]) => term)).toEqual(["1012", "1012AB"]);
    await act(async () => {
      second.resolve([ADDRESS_LINE]);
      await second.promise;
    });
    expect(options()).toEqual([ADDRESS_LINE.label]);
    await act(async () => {
      first.resolve([PC_LINE, PLAIN_NUMBER_LINE]);
      await first.promise;
    });
    expect(options()).toEqual([ADDRESS_LINE.label]);
  });

  it("an answer for an older term is dropped once the user typed on, even before the newer request went out", async () => {
    const first = linesDeferred();
    const onSuggest = vi.fn<Suggest>().mockReturnValueOnce(first.promise).mockResolvedValue([]);
    render(<Harness initial={suggestReady()} onSuggest={onSuggest} />);
    typePostal("1012");
    await advance(250);
    typePostal("1012A");
    await act(async () => {
      first.resolve([PC_LINE]);
      await first.promise;
    });
    expect(options()).toEqual([]);
    expect(postal()).toHaveAttribute("aria-expanded", "false");
  });

  it("an answer for an older term is dropped once the user erased the term below three characters", async () => {
    const first = linesDeferred();
    const onSuggest = vi.fn<Suggest>().mockReturnValueOnce(first.promise);
    render(<Harness initial={suggestReady()} onSuggest={onSuggest} />);
    typePostal("101");
    await advance(250);
    expect(onSuggest).toHaveBeenCalledTimes(1);
    typePostal("10");
    await act(async () => {
      first.resolve([PC_LINE]);
      await first.promise;
    });
    /* The field reads "10"; lines for "101" must not open. */
    expect(postal().value).toBe("10");
    expect(options()).toEqual([]);
    expect(postal()).toHaveAttribute("aria-expanded", "false");
  });

  it("an answer for an older term is dropped once the user cleared the postal code", async () => {
    const first = linesDeferred();
    const onSuggest = vi.fn<Suggest>().mockReturnValueOnce(first.promise);
    const onChange = vi.fn<(row: AddressRow) => void>();
    render(<Harness initial={suggestReady()} onChange={onChange} onSuggest={onSuggest} />);
    typePostal("1012");
    await advance(250);
    typePostal("", "deleteContentBackward");
    expect(onChange).toHaveBeenLastCalledWith({ ...suggestReady(), postalCode: "" });
    await act(async () => {
      first.resolve([PC_LINE]);
      await first.promise;
    });
    expect(postal().value).toBe("");
    expect(options()).toEqual([]);
    expect(postal()).toHaveAttribute("aria-expanded", "false");
  });

  it("an answer still in flight when the user picked a line is dropped: the popup stays closed after the pick", async () => {
    const second = linesDeferred();
    const onSuggest = vi.fn<Suggest>().mockResolvedValueOnce([PC_LINE]).mockReturnValueOnce(second.promise);
    render(<Harness initial={suggestReady()} onSuggest={onSuggest} />);
    typePostal("1012");
    await advance(250);
    expect(options()).toEqual([PC_LINE.label]);
    typePostal("1012A");
    await advance(250);
    expect(onSuggest).toHaveBeenCalledTimes(2);
    /* The user picks from the list still showing before the answer for "1012A" arrives. */
    fireEvent.click(option(PC_LINE.label));
    await advance(0);
    expect(postal().value).toBe("1012 AB");
    expect(options()).toEqual([]);
    await act(async () => {
      second.resolve([ADDRESS_LINE]);
      await second.promise;
    });
    expect(options()).toEqual([]);
    expect(postal()).toHaveAttribute("aria-expanded", "false");
  });

  it("picking an address line applies it through onChange, closes the popup and keeps the line's postal code (the combobox's own text reset does not empty it); nothing more is asked", async () => {
    const onSuggest = vi.fn<Suggest>().mockResolvedValue([PC_LINE, ADDRESS_LINE]);
    const onChange = vi.fn<(row: AddressRow) => void>();
    const initial: AddressRow = { ...suggestReady(), addressLine2: "Unit 3" };
    render(<Harness initial={initial} onChange={onChange} onSuggest={onSuggest} />);
    typePostal("1012ab");
    await advance(250);
    expect(options()).toEqual([PC_LINE.label, ADDRESS_LINE.label]);
    const typedCalls = onChange.mock.calls.length;

    fireEvent.click(option(ADDRESS_LINE.label));
    await advance(0);
    const picked: AddressRow = { ...initial, postalCode: "1012 AB", street: "Dam", houseNumber: "1", houseNumberSuffix: "A", city: "Amsterdam", latitude: "52.373", longitude: "4.893" };
    expect(onChange.mock.calls[typedCalls][0]).toEqual(picked);
    /* Whatever followed the pick did not touch the postal code. */
    for (const [row] of onChange.mock.calls.slice(typedCalls)) expect(row.postalCode).toBe("1012 AB");
    expect(onChange).toHaveBeenLastCalledWith(picked);
    expect(postal().value).toBe("1012 AB");
    expect(control("MasterData:Street").value).toBe("Dam");
    expect(control("MasterData:Nr").value).toBe("1");
    expect(control("MasterData:HouseNumberSuffix").value).toBe("A");
    expect(control("MasterData:City").value).toBe("Amsterdam");
    expect(control("MasterData:Latitude").value).toBe("52.373");
    expect(control("MasterData:Longitude").value).toBe("4.893");
    expect(control("MasterData:Building").value).toBe("Unit 3");
    expect(options()).toEqual([]);
    expect(postal()).toHaveAttribute("aria-expanded", "false");
    await advance(1000);
    expect(onSuggest).toHaveBeenCalledTimes(1);
    expect(postal().value).toBe("1012 AB");
  });

  it("picking a postal-code line keeps the typed number and suffix", async () => {
    const onSuggest = vi.fn<Suggest>().mockResolvedValue([PC_LINE, ADDRESS_LINE]);
    const onChange = vi.fn<(row: AddressRow) => void>();
    const initial: AddressRow = { ...suggestReady(), houseNumber: "7", houseNumberSuffix: "b" };
    render(<Harness initial={initial} onChange={onChange} onSuggest={onSuggest} />);
    typePostal("1012");
    await advance(250);
    fireEvent.click(option(PC_LINE.label));
    await advance(0);
    expect(onChange).toHaveBeenLastCalledWith({ ...initial, postalCode: "1012 AB", street: "Dam", city: "Amsterdam", latitude: "52.3731", longitude: "4.8932" });
    expect(control("MasterData:Nr").value).toBe("7");
    expect(control("MasterData:HouseNumberSuffix").value).toBe("b");
    expect(options()).toEqual([]);
  });

  it("typing on after a pick asks again for the new term", async () => {
    const onSuggest = vi.fn<Suggest>().mockResolvedValue([PC_LINE]);
    render(<Harness initial={suggestReady()} onSuggest={onSuggest} />);
    typePostal("1012");
    await advance(250);
    fireEvent.click(option(PC_LINE.label));
    await advance(0);
    expect(postal().value).toBe("1012 AB");
    typePostal("1012 AC");
    await advance(250);
    expect(onSuggest).toHaveBeenCalledTimes(2);
    expect(onSuggest).toHaveBeenLastCalledWith(expect.objectContaining({ postalCode: "1012 AC", street: "Dam" }), "1012 AC");
  });

  it("blur still runs the design-44 lookup with the typed postal code and marks the field touched", async () => {
    const onSuggest = vi.fn<Suggest>().mockResolvedValue([]);
    const onLookup = vi.fn<Lookup>().mockResolvedValue(FILL);
    render(<Harness initial={suggestReady()} onSuggest={onSuggest} onLookup={onLookup} />);
    typePostal("1012AB");
    fireEvent.blur(postal());
    expect(onLookup).toHaveBeenCalledTimes(1);
    expect(onLookup).toHaveBeenCalledWith({ ...suggestReady(), postalCode: "1012AB" });
    await advance(0);
    expect(values()).toEqual(FILLED_TEXT);
    expect(postal().value).toBe("1012AB");
  });

  it("an emptied postal code shows its required error after blur, on the combobox too", async () => {
    render(<Harness initial={{ ...suggestReady(), street: "Dam", city: "Amsterdam", postalCode: "1012" }} onSuggest={vi.fn<Suggest>().mockResolvedValue([])} />);
    expect(errors()).toEqual([]);
    typePostal("", "deleteContentBackward");
    fireEvent.blur(postal());
    await advance(0);
    expect(errors()).toEqual(["MasterData:PostalCodeRequired"]);
    expect(postal()).toHaveAttribute("aria-invalid", "true");
  });
});
