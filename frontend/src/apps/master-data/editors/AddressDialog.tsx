import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { Save } from "lucide-react";

import { EntityDialog, type RibbonCommand } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { NumberField, TextField, numberError } from "@/apps/master-data/components/fields";
import type { FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import { timeZoneOptions } from "@/apps/master-data/components/time-zones";
import { useFormState } from "@/apps/master-data/components/use-form-state";
import { addressTypes, countries } from "@/domains/master-data/entities";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The address of a relation (design 43, UI design: the Addresses tab opens this dialog on top of the
 * relation editor). The line is staged in the editor's form state and reaches the API with the
 * relation's Save. One address per address type: the type picker hides the types the relation's
 * other addresses use, and the rule is validated here too (AddressTypeAlreadyUsed). The time zone
 * follows the country when the user picks one and the zone is still empty. */

/** One address as the relation editor holds it (coordinates as typed text). */
export interface AddressRow {
  key: string;
  id: string | null;
  addressTypeId: string | null;
  addressTypeName: string;
  countryId: string | null;
  countryCode: string;
  street: string;
  houseNumber: string;
  addressLine2: string;
  postalCode: string;
  city: string;
  latitude: string;
  longitude: string;
  timeZoneId: string | null;
}

let rowCounter = 0;

export function newAddressRow(): AddressRow {
  return {
    key: `new-${++rowCounter}`,
    id: null,
    addressTypeId: null,
    addressTypeName: "",
    countryId: null,
    countryCode: "",
    street: "",
    houseNumber: "",
    addressLine2: "",
    postalCode: "",
    city: "",
    latitude: "",
    longitude: "",
    timeZoneId: null,
  };
}

export interface AddressDialogProps {
  open: boolean;
  /** The line being edited; null creates one. */
  row: AddressRow | null;
  /** The address types the relation's other addresses already use (design 43: one address per type). */
  usedAddressTypeIds: string[];
  readOnly: boolean;
  onClose: () => void;
  onSave: (row: AddressRow) => void;
}

export function AddressDialog({ open, row, usedAddressTypeIds, readOnly, onClose, onSave }: AddressDialogProps) {
  const { t } = useL();
  const formRef = useRef<HTMLFormElement>(null);
  const initial = useMemo(() => row ?? newAddressRow(), [row]);

  const validate = useCallback(
    (v: AddressRow): FieldErrors<AddressRow> => {
      const errors: FieldErrors<AddressRow> = {};
      if (!v.addressTypeId) errors.addressTypeId = "MasterData:AddressTypeRequired";
      else if (usedAddressTypeIds.includes(v.addressTypeId)) errors.addressTypeId = "MasterData:AddressTypeAlreadyUsed";
      if (!v.countryId) errors.countryId = "MasterData:CountryRequired";
      if (!v.street.trim()) errors.street = "MasterData:StreetRequired";
      if (!v.postalCode.trim()) errors.postalCode = "MasterData:PostalCodeRequired";
      if (!v.city.trim()) errors.city = "MasterData:CityRequired";
      const latitude = numberError(v.latitude, { min: -90, max: 90 });
      if (latitude) errors.latitude = latitude;
      const longitude = numberError(v.longitude, { min: -180, max: 180 });
      if (longitude) errors.longitude = longitude;
      return errors;
    },
    [usedAddressTypeIds],
  );

  const { form, isDirty, submit, setValues } = useFormState(initial, validate, readOnly);

  const typeLookup = addressTypes.useLookup(open);
  const countryLookup = countries.useLookup(open);
  const typeOptions = useMemo(
    () =>
      (typeLookup.data ?? [])
        .filter((item) => !usedAddressTypeIds.includes(item.id ?? ""))
        .map((item) => ({ value: item.id ?? "", label: item.name ?? "" })),
    [typeLookup.data, usedAddressTypeIds],
  );
  const countryOptions = useMemo(
    () => (countryLookup.data ?? []).map((c) => ({ value: c.id ?? "", label: `${c.code ?? ""} ${c.name ?? ""}`.trim() })),
    [countryLookup.data],
  );
  const zones = useMemo(timeZoneOptions, []);

  /* The country the zone should follow: set when the user picks a country, applied once its record arrives.
   * The zone follows the country while it is empty or still the zone a previous country filled in; a zone
   * the user chose stays. */
  const [zoneSource, setZoneSource] = useState<string | null>(null);
  const autoZone = useRef<string | null>(null);
  const zoneCountry = countries.useDetail(zoneSource ?? undefined);
  useEffect(() => {
    const dto = zoneCountry.data;
    if (zoneSource && dto?.id === zoneSource) {
      setZoneSource(null);
      const zone = dto.primaryTimeZoneId ?? null;
      if (zone) {
        const previous = autoZone.current;
        autoZone.current = zone;
        setValues((v) => (!v.timeZoneId || v.timeZoneId === previous ? { ...v, timeZoneId: zone } : v));
      }
    }
  }, [zoneCountry.data, zoneSource, setValues]);

  const handleSubmit = (e?: FormEvent) => {
    e?.preventDefault();
    if (readOnly) return;
    const values = submit(formRef.current);
    if (!values) return;
    const type = typeLookup.data?.find((item) => item.id === values.addressTypeId);
    const country = countryLookup.data?.find((c) => c.id === values.countryId);
    onSave({ ...values, addressTypeName: type?.name ?? values.addressTypeName, countryCode: country?.code ?? values.countryCode });
    onClose();
  };

  const commands: RibbonCommand[] = readOnly
    ? []
    : [{ id: "save", labelKey: "Save", icon: Save, isPrimary: true, onClick: () => formRef.current?.requestSubmit() }];

  return (
    <EntityDialog
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="editor"
      size="md"
      title={t(row ? "MasterData:EditAddress" : "MasterData:NewAddress")}
      commands={commands}
      isDirty={isDirty}
    >
      <form ref={formRef} onSubmit={handleSubmit} noValidate>
        <FormSection title={t("MasterData:Address")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <FormField label={t("MasterData:AddressType")} htmlFor={form.id("addressTypeId")} required error={form.error("addressTypeId")} floating="always">
              <Select
                items={typeOptions}
                value={form.values.addressTypeId}
                onValueChange={(next) => {
                  form.set("addressTypeId", (next as string | null) ?? null);
                  form.blur("addressTypeId");
                }}
                disabled={form.readOnly}
              >
                <SelectTrigger id={form.id("addressTypeId")} className="h-10 w-full bg-background" aria-invalid={form.error("addressTypeId") ? true : undefined}>
                  <SelectValue placeholder={t("MasterData:SelectAddressType")} />
                </SelectTrigger>
                <SelectContent>
                  {typeOptions.map((item) => (
                    <SelectItem key={item.value} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </FormField>
            <FormField label={t("MasterData:Country")} htmlFor={form.id("countryId")} required error={form.error("countryId")} floating="always">
              <ReferenceCombobox
                id={form.id("countryId")}
                value={form.values.countryId}
                onChange={(next) => {
                  form.set("countryId", next);
                  form.blur("countryId");
                  if (next) setZoneSource(next);
                }}
                options={countryOptions}
                required
                disabled={form.readOnly}
              />
            </FormField>
            <TextField form={form} field="street" labelKey="MasterData:Street" required maxLength={128} />
            <TextField form={form} field="houseNumber" labelKey="MasterData:HouseNumber" maxLength={16} />
            <TextField form={form} field="addressLine2" labelKey="MasterData:AddressLine2" maxLength={128} className="col-span-2" />
            <TextField form={form} field="postalCode" labelKey="MasterData:PostalCode" required maxLength={16} />
            <TextField form={form} field="city" labelKey="MasterData:City" required maxLength={128} />
            <NumberField form={form} field="latitude" labelKey="MasterData:Latitude" />
            <NumberField form={form} field="longitude" labelKey="MasterData:Longitude" />
            <FormField label={t("MasterData:TimeZone")} htmlFor={form.id("timeZoneId")} floating="always" className="col-span-2">
              <ReferenceCombobox
                id={form.id("timeZoneId")}
                value={form.values.timeZoneId}
                onChange={(next) => form.set("timeZoneId", next)}
                options={zones}
                disabled={form.readOnly}
              />
            </FormField>
          </div>
        </FormSection>
        <button type="submit" className="hidden" aria-hidden="true" tabIndex={-1} />
      </form>
    </EntityDialog>
  );
}
