import { useEffect, useMemo, useRef } from "react";

import { EntityLookup } from "@/components/ui/entity-lookup";
import { FormField } from "@/components/ui/form-field";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { useCreateInPlace, type CreatedRecord } from "@/domains/master-data/components/create-in-place";
import { relationLookupPage, relations, useRelationAddresses, type RelationDto, type RelationOption } from "@/domains/master-data/entities";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The relation, address and contact-person pickers the Transport app's forms and the trip type's defaults share (design
 * 61: the customer, the stops' relation and explicit address, the trip request's departure and arrival, a trip's
 * carrier; "Trip type defaults" 3 moved them here from the Transport app): an EntityLookup
 * over the relation lookup page (UI-LOOK-001/002/003, optionally narrowed to a role), the relation's addresses as a
 * ReferenceCombobox (a bounded list of one relation) and its contact persons likewise.
 *
 * Each picker offers "Add new …" (UI-LOOK-004) through the shared relation editor (src/domains/master-data/components):
 * a new relation (with the roles the picker narrows to, so the lookup finds it again), or a new address or contact
 * person of the picked relation, created in that relation's editor; after its save the new one is selected. */

export interface RelationFieldProps {
  id: string;
  label: string;
  value: RelationOption | null;
  onChange: (value: RelationOption | null) => void;
  /** Narrows the lookup to relations carrying one of these roles (a [Flags] value). */
  roles?: number;
  required?: boolean;
  disabled?: boolean;
  error?: string;
  className?: string;
}

export function RelationField({ id, label, value, onChange, roles, required, disabled, error, className }: RelationFieldProps) {
  const fetchPage = useMemo(() => relationLookupPage(roles), [roles]);
  const inPlace = useCreateInPlace();
  return (
    <>
      <FormField label={label} htmlFor={id} required={required} error={error} className={className} floating="always">
        <EntityLookup<RelationOption>
          id={id}
          value={value}
          onChange={onChange}
          fetchPage={fetchPage}
          getLabel={(r) => r.name}
          renderOption={(r) => (
            <>
              <div className="font-medium">{r.name}</div>
              <div className="text-label text-muted-foreground">{[r.code, [r.city, r.countryCode].filter(Boolean).join(" ")].filter(Boolean).join(", ")}</div>
            </>
          )}
          required={required}
          disabled={disabled}
          aria-invalid={error != null}
          {...inPlace.offer<RelationDto>("Relation", disabled === true, (r) => onChange(relationOptionOfDto(r.dto)), { roles })}
        />
      </FormField>
      {inPlace.dialog}
    </>
  );
}

/** A relation the editor saved, as a lookup option (its default address' city when it has one). */
export function relationOptionOfDto(dto: RelationDto): RelationOption {
  const address = (dto.addresses ?? []).find((a) => !a.addressTypeId) ?? dto.addresses?.[0];
  return {
    id: dto.id ?? "",
    code: dto.code ?? "",
    name: dto.name ?? "",
    city: address?.city ?? null,
    countryCode: null,
    roles: (dto.roles as number | undefined) ?? 0,
  };
}

/** A stored relation id with its name, as a lookup option (the rest of the option is unknown until picked again). */
export function relationOptionOf(id: string | null | undefined, name: string | null | undefined): RelationOption | null {
  return id ? { id, code: "", name: name ?? "", city: null, countryCode: null, roles: 0 } : null;
}

export interface AddressFieldProps {
  id: string;
  label: string;
  relationId: string | null | undefined;
  value: string | null;
  onChange: (value: string | null) => void;
  required?: boolean;
  disabled?: boolean;
  error?: string;
  className?: string;
  /** When the relation changes to one and the address is empty, select its default address (or its only one). On by
   * default: every relation/address pair in the Transport app wants it; a field that must stay empty passes false. */
  preselectDefault?: boolean;
  /** Muted help text under the control (UI-FORM-001), e.g. "Choose the relation first". */
  hint?: string;
}

/** The address to preselect for a picked relation: its default address (design 43: the one without an address type),
 * else its only address, else none. */
export function defaultAddressIdOf(addresses: readonly { id?: string | null; addressTypeId?: string | null }[] | undefined): string | null {
  if (!addresses || addresses.length === 0) return null;
  const fallback = addresses.find((a) => !a.addressTypeId) ?? (addresses.length === 1 ? addresses[0] : undefined);
  return fallback?.id ?? null;
}

/** One line per address: "Street 12, 1234 AB City (type)". */
export function addressLabel(address: { addressTypeName?: string | null; street?: string; houseNumber?: string | null; houseNumberSuffix?: string | null; postalCode?: string; city?: string }): string {
  const street = [address.street, [address.houseNumber, address.houseNumberSuffix].filter(Boolean).join("")].filter(Boolean).join(" ");
  const place = [address.postalCode, address.city].filter(Boolean).join(" ");
  const line = [street, place].filter(Boolean).join(", ");
  return address.addressTypeName ? `${line} (${address.addressTypeName})` : line;
}

export function AddressField({ id, label, relationId, value, onChange, required, disabled, error, className, preselectDefault = true, hint }: AddressFieldProps) {
  const addresses = useRelationAddresses(relationId ?? undefined);
  /* The default address follows a relation picked here, not a stored one on load (that would dirty an untouched form):
   * the relation the field mounted with counts as handled. */
  const handled = useRef(relationId ?? null);
  useEffect(() => {
    if (!preselectDefault || disabled || !relationId || handled.current === relationId) return;
    if (value) {
      handled.current = relationId;
      return;
    }
    if (!addresses.isSuccess) return;
    handled.current = relationId;
    const preselected = defaultAddressIdOf(addresses.data);
    if (preselected) onChange(preselected);
  }, [preselectDefault, disabled, relationId, value, addresses.isSuccess, addresses.data, onChange]);
  const options = useMemo(() => (addresses.data ?? []).map((a) => ({ value: a.id ?? "", label: addressLabel(a) })), [addresses.data]);
  const inPlace = useCreateInPlace();
  /* The relation editor opens on the picked relation; the address its save added is the one to select. */
  const known = new Set(options.map((o) => o.value));
  const onAddressCreated = (r: CreatedRecord<RelationDto>) => {
    const added = (r.dto.addresses ?? []).find((a) => a.id && !known.has(a.id));
    if (added?.id) onChange(added.id);
  };
  return (
    <>
      <FormField label={label} htmlFor={id} required={required} error={error} hint={hint} className={className} floating="always">
        <ReferenceCombobox
          id={id}
          value={value}
          onChange={onChange}
          options={options}
          required={required}
          disabled={disabled || !relationId}
          aria-invalid={error != null}
          {...inPlace.offer<RelationDto>("RelationAddress", disabled === true, onAddressCreated, { parentId: relationId })}
        />
      </FormField>
      {inPlace.dialog}
    </>
  );
}

export interface ContactPersonFieldProps {
  id: string;
  relationId: string | null | undefined;
  value: string | null;
  onChange: (value: string | null) => void;
  disabled?: boolean;
  className?: string;
  labelKey?: string;
  /** Muted help text under the control (UI-FORM-001), e.g. "Choose the carrier first". */
  hint?: string;
}

export function ContactPersonField({ id, relationId, value, onChange, disabled, className, labelKey = "Orders:CustomerContactPerson", hint }: ContactPersonFieldProps) {
  const { t } = useL();
  const relation = relations.useDetail(relationId ?? undefined);
  const options = useMemo(
    () => (relation.data?.contactPersons ?? []).map((c) => ({ value: c.id ?? "", label: [c.firstName, c.middleName, c.lastName].filter(Boolean).join(" ") })),
    [relation.data],
  );
  const inPlace = useCreateInPlace();
  /* The relation editor opens on the picked relation with the contact person dialog; the one its save added is selected. */
  const known = new Set(options.map((o) => o.value));
  const onContactCreated = (r: CreatedRecord<RelationDto>) => {
    const added = (r.dto.contactPersons ?? []).find((c) => c.id && !known.has(c.id));
    if (added?.id) onChange(added.id);
  };
  return (
    <>
      <FormField label={t(labelKey)} htmlFor={id} hint={hint} className={className} floating="always">
        <ReferenceCombobox
          id={id}
          value={value}
          onChange={onChange}
          options={options}
          disabled={disabled || !relationId}
          {...inPlace.offer<RelationDto>("RelationContactPerson", disabled === true, onContactCreated, { parentId: relationId })}
        />
      </FormField>
      {inPlace.dialog}
    </>
  );
}
