import { cloneElement, isValidElement, type ReactElement, type ReactNode } from "react";

import { Field, FieldDescription, FieldError, FieldLabel } from "@/components/ui/field";
import { cn } from "@/lib/utils";

/* Every input sits in a FormField with its label above (UI-FORM-001/002/006): a 12px
 * label, a 4px gap, the asterisk for required fields, the message under the control. */
export interface FormFieldProps {
  label: string;
  htmlFor: string;
  required?: boolean;
  error?: string;
  hint?: string;
  className?: string;
  children: ReactNode;
}

interface ControlAriaProps {
  "aria-required"?: boolean;
  "aria-invalid"?: boolean;
  "aria-describedby"?: string;
}

export function FormField({ label, htmlFor, required, error, hint, className, children }: FormFieldProps) {
  const errorId = `${htmlFor}-error`;
  const hintId = `${htmlFor}-hint`;
  const describedBy = error ? errorId : hint ? hintId : undefined;

  const control = isValidElement<ControlAriaProps>(children)
    ? cloneElement(children as ReactElement<ControlAriaProps>, {
        "aria-required": required || undefined,
        "aria-invalid": error ? true : undefined,
        "aria-describedby": describedBy,
      })
    : children;

  return (
    <Field data-invalid={error ? true : undefined} className={cn("gap-1", className)}>
      <FieldLabel htmlFor={htmlFor} className="text-label font-medium">
        {label}
        {required && <span aria-hidden="true"> *</span>}
      </FieldLabel>
      {control}
      {error ? (
        <FieldError id={errorId} className="text-label">
          {error}
        </FieldError>
      ) : hint ? (
        <FieldDescription id={hintId} className="text-label">
          {hint}
        </FieldDescription>
      ) : null}
    </Field>
  );
}
