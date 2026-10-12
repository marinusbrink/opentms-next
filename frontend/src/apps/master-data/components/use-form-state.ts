import { useMemo, useState, type Dispatch, type SetStateAction } from "react";

import { normaliseValues, type FieldErrors, type FormContext } from "@/apps/master-data/components/MasterDataEditor";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The form mechanics of a nested record dialog (design 43: the address and contact-person dialogs
 * inside the relation editor), the same as MasterDataEditor's: errors shown after blur or submit
 * (UI-FORM-006), dirty by value compare (UI-FORM-009). The dialog owns the row and the save. */

let instanceCounter = 0;

export interface FormState<TValues extends object> {
  form: FormContext<TValues>;
  errors: FieldErrors<TValues>;
  isDirty: boolean;
  /** Marks the form submitted; returns the values when valid, else focuses the first invalid field and returns null. */
  submit: (container: HTMLElement | null) => TValues | null;
  setValues: Dispatch<SetStateAction<TValues>>;
}

export function useFormState<TValues extends object>(
  initial: TValues,
  validate: (values: TValues) => FieldErrors<TValues>,
  readOnly: boolean,
): FormState<TValues> {
  const { t } = useL();
  const [instanceId] = useState(() => `sub${++instanceCounter}`);
  const [values, setValues] = useState<TValues>(initial);
  const [touched, setTouched] = useState<Set<string>>(new Set());
  const [submitted, setSubmitted] = useState(false);
  const errors = useMemo(() => validate(values), [validate, values]);
  const isDirty = JSON.stringify(normaliseValues(values)) !== JSON.stringify(normaliseValues(initial));

  const form: FormContext<TValues> = {
    values,
    set: (field, value) => setValues((v) => ({ ...v, [field]: value })),
    error: (field) => (touched.has(field) || submitted ? (errors[field] ? t(errors[field]!) : undefined) : undefined),
    blur: (field) => setTouched((s) => (s.has(field) ? s : new Set(s).add(field))),
    readOnly,
    id: (field) => `${instanceId}-${field}`,
  };

  const submit = (container: HTMLElement | null): TValues | null => {
    setSubmitted(true);
    const first = Object.keys(errors)[0];
    if (first) {
      requestAnimationFrame(() => container?.querySelector<HTMLElement>(`#${instanceId}-${first}`)?.focus());
      return null;
    }
    return values;
  };

  return { form, errors, isDirty, submit, setValues };
}
