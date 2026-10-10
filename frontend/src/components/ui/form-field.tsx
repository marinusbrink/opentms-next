import { cloneElement, isValidElement, type ReactElement, type ReactNode } from "react";

import { Field, FieldDescription, FieldError } from "@/components/ui/field";
import { cn } from "@/lib/utils";

/* Every input sits in a FormField (UI-FORM-001/002/006). The label floats inside the
 * field's outline (D-17 A, decided 2026-10-10): it reads as the placeholder while the field
 * is empty and unfocused, and sits on the top border once the field is focused or filled.
 * Controls without a placeholder (pickers, selects) keep the label on the border always. */
export interface FormFieldProps {
  label: string;
  htmlFor: string;
  required?: boolean;
  error?: string;
  hint?: string;
  /** "auto" (default) floats on focus or value; "always" keeps the label on the border. */
  floating?: "auto" | "always";
  className?: string;
  children: ReactNode;
}

interface ControlProps {
  "aria-required"?: boolean;
  "aria-invalid"?: boolean;
  "aria-describedby"?: string;
  placeholder?: string;
  className?: string;
}

const LABEL_BASE =
  "pointer-events-none absolute left-2.5 z-10 -translate-y-1/2 bg-background px-1 text-label font-medium text-muted-foreground transition-all duration-150";
const LABEL_FLOATED = "top-0";
const LABEL_RESTING =
  "peer-placeholder-shown:top-1/2 peer-placeholder-shown:bg-transparent peer-placeholder-shown:text-sm peer-placeholder-shown:font-normal peer-focus:top-0 peer-focus:bg-background peer-focus:text-label peer-focus:font-medium";

export function FormField({
  label,
  htmlFor,
  required,
  error,
  hint,
  floating = "auto",
  className,
  children,
}: FormFieldProps) {
  const errorId = `${htmlFor}-error`;
  const hintId = `${htmlFor}-hint`;
  const describedBy = error ? errorId : hint ? hintId : undefined;

  const control = isValidElement<ControlProps>(children)
    ? cloneElement(children as ReactElement<ControlProps>, {
        "aria-required": required || undefined,
        "aria-invalid": error ? true : undefined,
        "aria-describedby": describedBy,
        /* The floating mechanism needs a placeholder to detect "empty"; a single space keeps it invisible. */
        placeholder: floating === "auto" ? (children.props.placeholder ?? " ") : children.props.placeholder,
        className: cn("peer h-10 bg-background", floating === "auto" && "placeholder-transparent", children.props.className),
      })
    : children;

  return (
    <Field data-invalid={error ? true : undefined} className={cn("gap-1", className)}>
      <div className="relative">
        {control}
        <label
          htmlFor={htmlFor}
          data-slot="field-label"
          className={cn(
            LABEL_BASE,
            LABEL_FLOATED,
            floating === "auto" && LABEL_RESTING,
            error && "text-destructive peer-focus:text-destructive",
          )}
        >
          {label}
          {required && <span aria-hidden="true"> *</span>}
        </label>
      </div>
      {error ? (
        <FieldError id={errorId} className="px-1 text-label">
          {error}
        </FieldError>
      ) : hint ? (
        <FieldDescription id={hintId} className="px-1 text-label">
          {hint}
        </FieldDescription>
      ) : null}
    </Field>
  );
}
