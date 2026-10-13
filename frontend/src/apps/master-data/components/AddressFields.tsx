import { useRef, useState } from "react";

import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import {
  ReferenceCombobox,
  type ReferenceOption,
} from "@/components/ui/reference-combobox";
import { numberError } from "@/apps/master-data/components/fields";
import type { FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* One address of a relation, inline (design 43 as revised by the PO on 2026-10-13 after the old
 * screen): the same fields for the default address ("all addresses equal") and for every address
 * type's tab. Country shows the two-letter code and is searched; the house number keeps its suffix
 * apart; the second line is the building. An untouched address is no address: required fields
 * apply once something was entered (validateAddressRow). */

/** One address as the relation editor holds it (coordinates as typed text). */
export interface AddressRow {
  key: string;
  id: string | null;
  /** Null: the default address for every type without one of its own. */
  addressTypeId: string | null;
  countryId: string | null;
  street: string;
  houseNumber: string;
  houseNumberSuffix: string;
  addressLine2: string;
  postalCode: string;
  city: string;
  latitude: string;
  longitude: string;
  timeZoneId: string | null;
}

let rowCounter = 0;

export function newAddressRow(addressTypeId: string | null = null): AddressRow {
  return {
    key: `new-${++rowCounter}`,
    id: null,
    addressTypeId,
    countryId: null,
    street: "",
    houseNumber: "",
    houseNumberSuffix: "",
    addressLine2: "",
    postalCode: "",
    city: "",
    latitude: "",
    longitude: "",
    timeZoneId: null,
  };
}

/** Something was entered: the address counts and its required fields apply. */
export function hasAddressInput(row: AddressRow): boolean {
  /* The country and the time zone do not count by themselves: a new address starts with the tenant's default country and its zone. */
  return Boolean(
    row.street.trim() ||
    row.houseNumber.trim() ||
    row.houseNumberSuffix.trim() ||
    row.addressLine2.trim() ||
    row.postalCode.trim() ||
    row.city.trim() ||
    row.latitude.trim() ||
    row.longitude.trim(),
  );
}

export function validateAddressRow(row: AddressRow): FieldErrors<AddressRow> {
  const errors: FieldErrors<AddressRow> = {};
  if (!hasAddressInput(row)) return errors;
  if (!row.countryId) errors.countryId = "MasterData:CountryRequired";
  if (!row.street.trim()) errors.street = "MasterData:StreetRequired";
  if (!row.postalCode.trim())
    errors.postalCode = "MasterData:PostalCodeRequired";
  if (!row.city.trim()) errors.city = "MasterData:CityRequired";
  const latitude = numberError(row.latitude, { min: -90, max: 90 });
  if (latitude) errors.latitude = latitude;
  const longitude = numberError(row.longitude, { min: -180, max: 180 });
  if (longitude) errors.longitude = longitude;
  return errors;
}

export interface AddressFieldsProps {
  /** Prefix for the control ids, unique per address block. */
  idPrefix: string;
  row: AddressRow;
  onChange: (row: AddressRow) => void;
  readOnly: boolean;
  /** The editor was submitted: show every error, not only the touched fields'. */
  showErrors: boolean;
  countryOptions: ReferenceOption[];
  zoneOptions: ReferenceOption[];
  /** The user picked a country: the caller lets the time zone follow it. */
  onCountryPicked: (countryId: string) => void;
  /** Design 44: asked when the postal code or the house number loses focus; fills only the empty fields. Absent while the feature is off. */
  onLookup?: (row: AddressRow) => Promise<AddressLookupFill | null>;
}

/** What a lookup may fill in: the editor keeps what the user typed. */
export interface AddressLookupFill {
  street?: string | null;
  houseNumber?: string | null;
  city?: string | null;
  latitude?: number | null;
  longitude?: number | null;
}

/** The fields the lookup fills when they are still empty (design 44). */
export function applyLookup(
  row: AddressRow,
  fill: AddressLookupFill,
): AddressRow {
  const text = (current: string, value: string | null | undefined) =>
    current.trim() || !value ? current : value;
  const number = (current: string, value: number | null | undefined) =>
    current.trim() || value == null ? current : String(value);
  return {
    ...row,
    street: text(row.street, fill.street),
    houseNumber: text(row.houseNumber, fill.houseNumber),
    city: text(row.city, fill.city),
    latitude: number(row.latitude, fill.latitude),
    longitude: number(row.longitude, fill.longitude),
  };
}

type TextKey =
  | "street"
  | "houseNumber"
  | "houseNumberSuffix"
  | "addressLine2"
  | "postalCode"
  | "city"
  | "latitude"
  | "longitude";

export function AddressFields({
  idPrefix,
  row,
  onChange,
  readOnly,
  showErrors,
  countryOptions,
  zoneOptions,
  onCountryPicked,
  onLookup,
}: AddressFieldsProps) {
  const { t } = useL();
  const [touched, setTouched] = useState<Set<string>>(new Set());
  const errors = validateAddressRow(row);
  const required = hasAddressInput(row);
  const error = (field: keyof AddressRow & string) =>
    (touched.has(field) || showErrors) && errors[field]
      ? t(errors[field]!)
      : undefined;
  const touch = (field: string) =>
    setTouched((s) => (s.has(field) ? s : new Set(s).add(field)));
  const rowRef = useRef(row);
  rowRef.current = row;
  const [lookingUp, setLookingUp] = useState(false);
  /* The lookup runs on the latest row and fills only what is still empty when it answers; a failure is silent. */
  const lookup = async () => {
    const current = rowRef.current;
    if (
      !onLookup ||
      readOnly ||
      !current.countryId ||
      !current.postalCode.trim()
    )
      return;
    setLookingUp(true);
    try {
      const fill = await onLookup(current);
      if (fill) onChange(applyLookup(rowRef.current, fill));
    } catch {
      /* nothing to fill */
    } finally {
      setLookingUp(false);
    }
  };
  const id = (field: string) => `${idPrefix}-${field}`;

  const text = (
    field: TextKey,
    labelKey: string,
    className: string,
    maxLength: number,
    isRequired = false,
  ) => (
    <FormField
      label={t(labelKey)}
      htmlFor={id(field)}
      required={isRequired && required}
      error={error(field)}
      className={className}
    >
      <Input
        id={id(field)}
        value={row[field]}
        maxLength={maxLength}
        disabled={readOnly}
        inputMode={
          field === "latitude" || field === "longitude" ? "decimal" : undefined
        }
        onChange={(e) => onChange({ ...row, [field]: e.target.value })}
        onBlur={() => {
          touch(field);
          if (field === "postalCode" || field === "houseNumber") void lookup();
        }}
      />
    </FormField>
  );

  return (
    <div
      data-slot="address-fields"
      aria-busy={lookingUp || undefined}
      className="grid grid-cols-12 gap-x-3 gap-y-2"
    >
      <FormField
        label={t("MasterData:Country")}
        htmlFor={id("countryId")}
        required={required}
        error={error("countryId")}
        floating="always"
        className="col-span-2"
      >
        <ReferenceCombobox
          id={id("countryId")}
          value={row.countryId}
          onChange={(next) => {
            onChange({ ...row, countryId: next });
            touch("countryId");
            if (next) onCountryPicked(next);
          }}
          options={countryOptions}
          disabled={readOnly}
        />
      </FormField>
      {text("postalCode", "MasterData:PostalCode", "col-span-3", 16, true)}
      {text("city", "MasterData:City", "col-span-4", 128, true)}
      <FormField
        label={t("MasterData:TimeZone")}
        htmlFor={id("timeZoneId")}
        floating="always"
        className="col-span-3"
      >
        <ReferenceCombobox
          id={id("timeZoneId")}
          value={row.timeZoneId}
          onChange={(next) => onChange({ ...row, timeZoneId: next })}
          options={zoneOptions}
          disabled={readOnly}
        />
      </FormField>
      {text("street", "MasterData:Street", "col-span-8", 128, true)}
      {text("houseNumber", "MasterData:Nr", "col-span-2", 16)}
      {text(
        "houseNumberSuffix",
        "MasterData:HouseNumberSuffix",
        "col-span-2",
        16,
      )}
      {text("addressLine2", "MasterData:Building", "col-span-8", 128)}
      {text("longitude", "MasterData:Longitude", "col-span-2", 20)}
      {text("latitude", "MasterData:Latitude", "col-span-2", 20)}
    </div>
  );
}
