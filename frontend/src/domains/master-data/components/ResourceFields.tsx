import { useMemo } from "react";

import { EntityLookup } from "@/components/ui/entity-lookup";
import { FormField } from "@/components/ui/form-field";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { useCreateInPlace, type CreatedRecord } from "@/domains/master-data/components/create-in-place";
import { driverLookupPage, vehicleCombinations, vehicleLookupPage, type DriverOption, type VehicleDto, type VehicleOption } from "@/domains/master-data/entities";

/* The trip resource pickers (vehicle combination, vehicles, drivers) the trip page's resource dialog, the order's Tevens
 * trip, the New trip dialog and the trip type's defaults share (design 61 "Trip type defaults" 3: one picker per
 * resource, reused). Each offers "Add new …" (UI-LOOK-004) and the pencil (UI-LOOK-005) through create-in-place; a
 * record created in place is handed to `onAdd` (multi-value) or `onChange` (single), which callers keep stale-safe
 * because the editor saves after this render. Combination and vehicles are exclusive: the caller clears the other. */

/** The vehicle a picker shows for a record its editor saved. */
function vehicleOptionOfCreated(r: CreatedRecord<VehicleDto>): VehicleOption {
  return { id: r.id, name: r.name, licencePlate: r.dto.licencePlate ?? null, kind: r.dto.kind ?? 1, vehicleTypeName: r.dto.vehicleTypeName ?? null };
}

/** The driver a picker shows for a record its editor saved. */
function driverOptionOfCreated(r: CreatedRecord): DriverOption {
  return { id: r.id, name: r.name, code: r.code ?? "" };
}

export interface VehicleCombinationFieldProps {
  id: string;
  label: string;
  value: string | null;
  onChange: (value: string | null) => void;
  /** A combination created in place (default: `onChange`); a caller whose `onChange` reads its render's values passes a
   *  stale-safe one. */
  onCreated?: (id: string) => void;
  /** A stored combination the lookup no longer lists (archived): kept with its name (design 51). */
  stored?: { id: string | null | undefined; name: string | null | undefined };
  disabled?: boolean;
  /** No "Add new …" row (a business state, e.g. vehicles are chosen), on top of `disabled`. */
  createDisabled?: boolean;
  hint?: string;
  className?: string;
}

export function VehicleCombinationField({ id, label, value, onChange, onCreated, stored, disabled, createDisabled, hint, className }: VehicleCombinationFieldProps) {
  const lookup = vehicleCombinations.useLookup();
  const options = useMemo(() => {
    const all = (lookup.data ?? []).map((item) => ({ value: item.id ?? "", label: item.name ?? item.code ?? "" }));
    if (stored?.id && !all.some((o) => o.value === stored.id)) all.push({ value: stored.id, label: stored.name ?? "" });
    return all;
  }, [lookup.data, stored?.id, stored?.name]);
  const inPlace = useCreateInPlace();
  return (
    <>
      <FormField label={label} htmlFor={id} hint={hint} className={className} floating="always">
        <ReferenceCombobox id={id} value={value} onChange={onChange} options={options} disabled={disabled} {...inPlace.offer("VehicleCombination", disabled === true || createDisabled === true, (r) => (onCreated ?? onChange)(r.id))} />
      </FormField>
      {inPlace.dialog}
    </>
  );
}

export interface VehiclesFieldProps {
  id: string;
  label: string;
  value: VehicleOption[];
  onChange: (value: VehicleOption[]) => void;
  /** A vehicle created in place joins the selection (at most `max`). */
  onAdd: (vehicle: VehicleOption) => void;
  /** MaxVehiclesPerTrip (design 59). */
  max: number;
  disabled?: boolean;
  createDisabled?: boolean;
  hint?: string;
  className?: string;
}

export function VehiclesField({ id, label, value, onChange, onAdd, max, disabled, createDisabled, hint, className }: VehiclesFieldProps) {
  const fetchPage = useMemo(() => vehicleLookupPage(), []);
  const inPlace = useCreateInPlace();
  return (
    <>
      <FormField label={label} htmlFor={id} hint={hint} className={className} floating="always">
        <EntityLookup<VehicleOption>
          id={id}
          multiple
          value={value}
          onChange={(next) => onChange(next.slice(0, max))}
          fetchPage={fetchPage}
          getLabel={(v) => v.name}
          disabled={disabled}
          {...inPlace.offer<VehicleDto>("Vehicle", disabled === true || createDisabled === true || value.length >= max, (r) => onAdd(vehicleOptionOfCreated(r)))}
        />
      </FormField>
      {inPlace.dialog}
    </>
  );
}

export interface DriversFieldProps {
  id: string;
  label: string;
  value: DriverOption[];
  onChange: (value: DriverOption[]) => void;
  /** A driver created in place joins the selection (at most `max`). */
  onAdd: (driver: DriverOption) => void;
  /** MaxDriversPerTrip (design 59). */
  max: number;
  disabled?: boolean;
  createDisabled?: boolean;
  hint?: string;
  className?: string;
}

export function DriversField({ id, label, value, onChange, onAdd, max, disabled, createDisabled, hint, className }: DriversFieldProps) {
  const inPlace = useCreateInPlace();
  return (
    <>
      <FormField label={label} htmlFor={id} hint={hint} className={className} floating="always">
        <EntityLookup<DriverOption>
          id={id}
          multiple
          value={value}
          onChange={(next) => onChange(next.slice(0, max))}
          fetchPage={driverLookupPage}
          getLabel={(d) => d.name}
          disabled={disabled}
          {...inPlace.offer("Driver", disabled === true || createDisabled === true || value.length >= max, (r) => onAdd(driverOptionOfCreated(r)))}
        />
      </FormField>
      {inPlace.dialog}
    </>
  );
}
