import { useCallback, useMemo, useRef, useState, type Dispatch, type SetStateAction } from "react";

import { useL } from "@/lib/i18n/LocalizationProvider";

/* The form mechanics every record screen shares (UI-FORM-006: errors after blur or submit; UI-FORM-009: dirty by
 * value compare after normalising empty strings to null). The screen owns the values, the validation and the save;
 * this hook owns touched/submitted state, the error texts and the focus on the first invalid field. The one form
 * state of the frontend: the Transport app's pages (design 61) and the nested record dialogs (a relation's contact
 * person) use it; MasterDataEditor carries the same mechanics inside its dialog over these types. */

export type FieldErrors<TValues> = Partial<Record<keyof TValues & string, string>>;

export interface FormContext<TValues extends object> {
  values: TValues;
  set: <K extends keyof TValues & string>(field: K, value: TValues[K]) => void;
  /** A change computed from the latest values. */
  update: (change: (values: TValues) => TValues) => void;
  /** The error to show under a field: only after blur or submit (UI-FORM-006). */
  error: (field: keyof TValues & string) => string | undefined;
  blur: (field: keyof TValues & string) => void;
  readOnly: boolean;
  /** Prefix for control ids, unique per form instance. */
  id: (field: string) => string;
}

/** Empty strings become null, recursively, so dirty compares values and not touches (UI-FORM-009). */
export const normaliseValues = (value: unknown): unknown =>
  typeof value === "string"
    ? value.trim() || null
    : Array.isArray(value)
      ? value.map(normaliseValues)
      : value && typeof value === "object"
        ? Object.fromEntries(Object.entries(value).map(([k, v]) => [k, normaliseValues(v)]))
        : value;

export function isSameValue(a: unknown, b: unknown): boolean {
  return JSON.stringify(normaliseValues(a)) === JSON.stringify(normaliseValues(b));
}

let instanceCounter = 0;

export interface FormState<TValues extends object> {
  form: FormContext<TValues>;
  errors: FieldErrors<TValues>;
  isDirty: boolean;
  /** Marks the form submitted; returns the values when valid, else focuses the first invalid field and returns null. */
  submit: (container: HTMLElement | null) => TValues | null;
  /** The first invalid field, for the caller that switches to its tab (UI-FORM-006). */
  firstErrorField: () => (keyof TValues & string) | undefined;
  setValues: Dispatch<SetStateAction<TValues>>;
  /** Replaces the values and the baseline (after a save or a load), so the form is clean again. */
  reset: (values: TValues) => void;
}

export function useFormState<TValues extends object>(initial: TValues, validate: (values: TValues) => FieldErrors<TValues>, readOnly: boolean): FormState<TValues> {
  const { t } = useL();
  const [instanceId] = useState(() => `f${++instanceCounter}`);
  const [values, setValues] = useState<TValues>(initial);
  const [baseline, setBaseline] = useState<TValues>(initial);
  const [touched, setTouched] = useState<Set<string>>(new Set());
  const [submitted, setSubmitted] = useState(false);
  const errors = useMemo(() => validate(values), [validate, values]);
  const isDirty = !isSameValue(values, baseline);
  const errorsRef = useRef(errors);
  errorsRef.current = errors;

  const form: FormContext<TValues> = {
    values,
    set: (field, value) => setValues((v) => ({ ...v, [field]: value })),
    update: (change) => setValues(change),
    error: (field) => (touched.has(field) || submitted ? (errors[field] ? t(errors[field]!) : undefined) : undefined),
    blur: (field) => setTouched((s) => (s.has(field) ? s : new Set(s).add(field))),
    readOnly,
    id: (field) => `${instanceId}-${field}`,
  };

  const firstErrorField = useCallback(() => Object.keys(errorsRef.current)[0] as (keyof TValues & string) | undefined, []);

  const submit = (container: HTMLElement | null): TValues | null => {
    setSubmitted(true);
    const first = firstErrorField();
    if (first) {
      requestAnimationFrame(() => container?.querySelector<HTMLElement>(`#${instanceId}-${first}`)?.focus());
      return null;
    }
    return values;
  };

  const reset = useCallback((next: TValues) => {
    setValues(next);
    setBaseline(next);
    setTouched(new Set());
    setSubmitted(false);
  }, []);

  return { form, errors, isDirty, submit, firstErrorField, setValues, reset };
}

/** "12,5" or "12.5" → 12.5; empty → null; anything else → NaN (the validator reports it). */
export function parseNumber(value: string | null | undefined): number | null {
  const text = (value ?? "").trim().replace(",", ".");
  if (!text) return null;
  const n = Number(text);
  return Number.isFinite(n) ? n : NaN;
}

export function parseInteger(value: string | null | undefined): number | null {
  const n = parseNumber(value);
  return n == null || Number.isNaN(n) ? n : Number.isInteger(n) ? n : NaN;
}

export function numberToText(value: number | null | undefined): string {
  return value == null ? "" : String(value);
}

/** The validation key for a numeric text: undefined when empty or a number within the bounds. */
export function numberError(value: string | null | undefined, options?: { integer?: boolean; min?: number; max?: number }): string | undefined {
  const n = options?.integer ? parseInteger(value) : parseNumber(value);
  if (n == null) return undefined;
  if (Number.isNaN(n)) return "MasterData:InvalidNumber";
  if (options?.min != null && n < options.min) return "MasterData:InvalidNumber";
  if (options?.max != null && n > options.max) return "MasterData:InvalidNumber";
  return undefined;
}
