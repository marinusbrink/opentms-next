import { useMemo, useState, type ReactNode } from "react";

import { Button } from "@/components/ui/button";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { FormField } from "@/components/ui/form-field";
import { ReferenceCombobox, type ReferenceOption } from "@/components/ui/reference-combobox";
import { StatusBadge } from "@/components/ui/status-badge";
import { AddressField, addressLabel, relationOptionOfDto } from "@/domains/master-data/components/RelationFields";
import { useCreateInPlace, type CreatableEntity } from "@/domains/master-data/components/create-in-place";
import type { RelationInitialAddress } from "@/domains/master-data/components/RelationEditor";
import { relationLookupPage, useRelationAddresses, type RelationDto, type RelationOption } from "@/domains/master-data/entities";
import {
  INTAKE_RESOLUTION_METHODS,
  INTAKE_RESOLUTION_STATUSES,
  intakeEnumLabel,
  type OrderIntakeFieldResolution,
} from "@/domains/orders/order-intakes";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* One resolved field of the review (design 72 "Review"): the label, what the document said, the resolution's state as a
 * StatusBadge (green resolved, amber ambiguous with candidates, red unresolved; tone tokens, UI-TOK-005) with its
 * method, the candidates as one-click picks, and the picker. A pick made here counts as resolved by the user. */

/** A pick of the clerk: the row's id, and its name for the line under the badge. */
export interface Pick {
  id: string;
  name: string;
}

const BORDER: Record<string, string> = {
  Resolved: "border-l-tone-success",
  Ambiguous: "border-l-tone-warning",
  Unresolved: "border-l-tone-danger",
};

export interface ResolutionRowProps {
  label: string;
  path: string;
  resolution: OrderIntakeFieldResolution | undefined;
  /** What the document said for this field, as text. */
  received: string;
  pick: Pick | undefined;
  onPick: (pick: Pick | undefined) => void;
  readOnly: boolean;
  /** The picker (EntityLookup / ReferenceCombobox) for this field. */
  children?: ReactNode;
}

export function ResolutionRow({ label, path, resolution, received, pick, onPick, readOnly, children }: ResolutionRowProps) {
  const { t } = useL();
  const status = pick ? "Resolved" : resolution?.status != null ? INTAKE_RESOLUTION_STATUSES[resolution.status] : undefined;
  const method = pick ? intakeEnumLabel(t, "IntakeResolutionMethod", INTAKE_RESOLUTION_METHODS, 7) : resolution?.method != null ? intakeEnumLabel(t, "IntakeResolutionMethod", INTAKE_RESOLUTION_METHODS, resolution.method) : "";
  const target = pick?.name ?? resolution?.targetName ?? "";
  const candidates = !readOnly && !pick && resolution?.status !== 1 ? (resolution?.candidates ?? []) : [];
  return (
    <div className={cn("grid gap-2 rounded-lg border border-l-4 bg-background p-3", status ? BORDER[status] : "border-l-border")} data-path={path} data-status={status}>
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-sm font-medium">{label}</span>
        {status && <StatusBadge entity="IntakeResolution" state={status} />}
        {method && <span className="text-label text-muted-foreground">{method}</span>}
      </div>
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        <dt className="text-label text-muted-foreground">{t("Orders:IntakeReceived")}</dt>
        <dd className="min-w-0 break-words">{received || <span className="text-muted-foreground">{t("Orders:IntakeNothingReceived")}</span>}</dd>
        {target && (
          <>
            <dt className="text-label text-muted-foreground">{t("Orders:IntakeResolvedTo")}</dt>
            <dd className="min-w-0 break-words font-medium">{target}</dd>
          </>
        )}
      </dl>
      {candidates.length > 0 && (
        <div className="flex flex-wrap items-center gap-2" aria-label={t("Orders:IntakeCandidates")}>
          <span className="text-label text-muted-foreground">{t("Orders:IntakeCandidates")}</span>
          {candidates.map((candidate) => (
            <Button key={candidate.id} type="button" variant="outline" size="sm" onClick={() => onPick({ id: candidate.id ?? "", name: candidate.name ?? "" })}>
              {candidate.name}
              {candidate.detail && <span className="text-muted-foreground">{candidate.detail}</span>}
            </Button>
          ))}
        </div>
      )}
      {!readOnly && children}
    </div>
  );
}

/* ── Pickers ─────────────────────────────────────────────────────────────────────────────────────────────────── */

export interface RelationPickerProps {
  id: string;
  label: string;
  value: RelationOption | null;
  onChange: (value: RelationOption | null) => void;
  /** A new relation's roles (a customer picker creates a customer). */
  roles?: number;
  /** "Add new …" opens the relation editor with this name when nothing was typed (the name as received). */
  receivedName: string;
  /** "Add new …" starts the new relation with this address (a stop's address as received). */
  receivedAddress?: RelationInitialAddress;
}

/** A relation lookup whose "Add new …" (UI-LOOK-004) prefills the received name. */
export function RelationPicker({ id, label, value, onChange, roles, receivedName, receivedAddress }: RelationPickerProps) {
  const fetchPage = useMemo(() => relationLookupPage(roles), [roles]);
  const inPlace = useCreateInPlace();
  const offer = inPlace.offer<RelationDto>("Relation", false, (r) => onChange(relationOptionOfDto(r.dto)), { roles, address: receivedAddress });
  return (
    <>
      <FormField label={label} htmlFor={id} floating="always">
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
          {...offer}
          onCreateNew={offer.onCreateNew ? (term) => offer.onCreateNew!(term.trim() || receivedName) : undefined}
        />
      </FormField>
      {inPlace.dialog}
    </>
  );
}

export interface ReferencePickerProps {
  id: string;
  label: string;
  entity: CreatableEntity;
  options: ReferenceOption[];
  value: string | null;
  onChange: (pick: Pick | undefined) => void;
  /** "Add new …" opens the entity's editor with this name when nothing was typed. */
  receivedName: string;
}

/** A pick from a reference list (order type, waypoint type, transport unit, product, temperature) with "Add new …". */
export function ReferencePicker({ id, label, entity, options, value, onChange, receivedName }: ReferencePickerProps) {
  const inPlace = useCreateInPlace();
  const offer = inPlace.offer(entity, false, (r) => onChange({ id: r.id, name: r.name }));
  return (
    <>
      <FormField label={label} htmlFor={id} floating="always">
        <ReferenceCombobox
          id={id}
          value={value}
          onChange={(next) => onChange(next ? { id: next, name: options.find((o) => o.value === next)?.label ?? "" } : undefined)}
          options={options}
          {...offer}
          onCreateNew={offer.onCreateNew ? (term) => offer.onCreateNew!(term.trim() || receivedName) : undefined}
        />
      </FormField>
      {inPlace.dialog}
    </>
  );
}

export interface AddressPickerProps {
  id: string;
  /** The picked address id. */
  value: string | null;
  onChange: (pick: Pick | undefined) => void;
  /** "Add new …" on the relation opens the relation editor with the received address name and address. */
  receivedName: string;
  receivedAddress?: RelationInitialAddress;
}

/** A stop's address: first the relation (with "Add new …"), then one of its addresses (with "Add new address …"). */
export function AddressPicker({ id, value, onChange, receivedName, receivedAddress }: AddressPickerProps) {
  const { t } = useL();
  const [relation, setRelation] = useState<RelationOption | null>(null);
  const addresses = useRelationAddresses(relation?.id);
  return (
    <div className="grid grid-cols-2 gap-x-4 gap-y-3">
      <RelationPicker id={`${id}-relation`} label={t("Orders:IntakePickRelation")} value={relation} onChange={setRelation} receivedName={receivedName} receivedAddress={receivedAddress} />
      <AddressField
        id={`${id}-address`}
        label={t("Orders:IntakePickAddress")}
        relationId={relation?.id}
        value={value}
        onChange={(next) => {
          const address = addresses.data?.find((a) => a.id === next);
          onChange(next ? { id: next, name: [relation?.name, address ? addressLabel(address) : null].filter(Boolean).join(", ") } : undefined);
        }}
        hint={relation ? undefined : t("Orders:IntakePickRelationFirst")}
      />
    </div>
  );
}
