import { useMemo } from "react";

import { FormField } from "@/components/ui/form-field";
import { ReferenceCombobox, type ReferenceOption } from "@/components/ui/reference-combobox";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import { currencies } from "@/domains/master-data/entities";

/* The currency picker of the Transport forms (UI-LOOK-001: currencies are a static list of more than 15 items, a
 * Combobox with client-side search): the order's currency and the agreed purchase currency of a "Tevens trip". The DTOs
 * carry the ISO 4217 code, not the currency's id, so the option value is the code (the currencies lookup's `code`).
 * "Add new …" opens the currency editor on top (UI-LOOK-004) and picks the saved currency's code. A stored code that
 * is no longer in the lookup (an inactive currency) stays selectable under its code, so an untouched form stays clean. */

export interface CurrencyFieldProps {
  id: string;
  label: string;
  /** The ISO code, upper case; "" or null = none. */
  value: string | null;
  onChange: (code: string | null) => void;
  required?: boolean;
  /** Read-only form or a business-state block: no change and no "Add new …". */
  disabled?: boolean;
  error?: string;
}

export function CurrencyField({ id, label, value, onChange, required, disabled = false, error }: CurrencyFieldProps) {
  const lookup = currencies.useLookup();
  const inPlace = useCreateInPlace();
  const code = value?.trim().toUpperCase() || null;
  const options = useMemo<ReferenceOption[]>(() => {
    const list = (lookup.data ?? [])
      .filter((c) => c.code)
      .map((c) => ({ value: c.code!.toUpperCase(), label: `${c.code!.toUpperCase()} ${c.name ?? ""}`.trim() }));
    if (code && !list.some((o) => o.value === code)) list.push({ value: code, label: code });
    return list;
  }, [lookup.data, code]);

  return (
    <>
    <FormField label={label} htmlFor={id} required={required} error={error} floating="always">
      <ReferenceCombobox
        id={id}
        value={code}
        onChange={(next) => onChange(next)}
        options={options}
        required={required}
        disabled={disabled}
        aria-invalid={error != null}
        {...inPlace.offer("Currency", disabled, (r) => {
          const created = (r.code ?? (r.dto as { isoCode?: string | null }).isoCode ?? "").toUpperCase();
          if (created) onChange(created);
        })}
      />
    </FormField>
    {inPlace.dialog}
    </>
  );
}
