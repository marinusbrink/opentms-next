import { useMemo } from "react";

import { EntityLookup } from "@/components/ui/entity-lookup";
import { FormField } from "@/components/ui/form-field";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { relationLookupPage, relations, useRelationAddresses, type RelationOption } from "@/domains/master-data/entities";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The relation, address and contact-person pickers the Transport app's forms share (design 61: the customer, the
 * stops' relation and explicit address, the trip request's departure and arrival, a trip's carrier): an EntityLookup
 * over the relation lookup page (UI-LOOK-001/002/003, optionally narrowed to a role), the relation's addresses as a
 * ReferenceCombobox (a bounded list of one relation) and its contact persons likewise.
 *
 * DEVIATION(UI-LOOK-004): the pickers offer no "Add new relation…": the relation editor lives in the Master data app
 * (src/apps/master-data/editors/RelationEditor.tsx) and an app never imports another app (CLAUDE.md, frontend
 * structure); moving the editor to src/domains/master-data/components is the Master data builder's folder. Reported
 * in design 61's build notes as an open question; the row returns the moment the editor is shared. */

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
  return (
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
      />
    </FormField>
  );
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
}

/** One line per address: "Street 12, 1234 AB City (type)". */
export function addressLabel(address: { addressTypeName?: string | null; street?: string; houseNumber?: string | null; houseNumberSuffix?: string | null; postalCode?: string; city?: string }): string {
  const street = [address.street, [address.houseNumber, address.houseNumberSuffix].filter(Boolean).join("")].filter(Boolean).join(" ");
  const place = [address.postalCode, address.city].filter(Boolean).join(" ");
  const line = [street, place].filter(Boolean).join(", ");
  return address.addressTypeName ? `${line} (${address.addressTypeName})` : line;
}

export function AddressField({ id, label, relationId, value, onChange, required, disabled, error, className }: AddressFieldProps) {
  const addresses = useRelationAddresses(relationId ?? undefined);
  const options = useMemo(() => (addresses.data ?? []).map((a) => ({ value: a.id ?? "", label: addressLabel(a) })), [addresses.data]);
  return (
    <FormField label={label} htmlFor={id} required={required} error={error} className={className} floating="always">
      <ReferenceCombobox id={id} value={value} onChange={onChange} options={options} required={required} disabled={disabled || !relationId} aria-invalid={error != null} />
    </FormField>
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
}

export function ContactPersonField({ id, relationId, value, onChange, disabled, className, labelKey = "Orders:CustomerContactPerson" }: ContactPersonFieldProps) {
  const { t } = useL();
  const relation = relations.useDetail(relationId ?? undefined);
  const options = useMemo(
    () => (relation.data?.contactPersons ?? []).map((c) => ({ value: c.id ?? "", label: [c.firstName, c.middleName, c.lastName].filter(Boolean).join(" ") })),
    [relation.data],
  );
  return (
    <FormField label={t(labelKey)} htmlFor={id} className={className} floating="always">
      <ReferenceCombobox id={id} value={value} onChange={onChange} options={options} disabled={disabled || !relationId} />
    </FormField>
  );
}
