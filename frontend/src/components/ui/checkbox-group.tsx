import { Checkbox } from "@/components/ui/checkbox";
import { cn } from "@/lib/utils";

/* A small group of booleans that together form one field (design 43: the relation roles). Each row
 * is a Checkbox with its label to the right as one click target (UI-FORM-010). The group sits in a
 * FormField like any control: it receives aria-describedby / aria-invalid / className by cloneElement
 * and is named by the FormField's label through `aria-labelledby` (`<id>-label`). */

export interface CheckboxGroupOption {
  id: string;
  label: string;
}

export interface CheckboxGroupProps {
  id: string;
  value: string[];
  onChange: (value: string[]) => void;
  options: CheckboxGroupOption[];
  disabled?: boolean;
  columns?: 1 | 2;
  "aria-invalid"?: boolean;
  "aria-describedby"?: string;
  "aria-labelledby"?: string;
  "aria-required"?: boolean;
  className?: string;
}

export function CheckboxGroup({
  id,
  value,
  onChange,
  options,
  disabled = false,
  columns = 1,
  "aria-invalid": ariaInvalid,
  "aria-describedby": ariaDescribedBy,
  "aria-labelledby": ariaLabelledBy,
  "aria-required": ariaRequired,
  className,
}: CheckboxGroupProps) {
  const toggle = (optionId: string, checked: boolean) => {
    if (checked) {
      if (!value.includes(optionId)) onChange([...value, optionId]);
    } else if (value.includes(optionId)) {
      onChange(value.filter((v) => v !== optionId));
    }
  };

  return (
    <div
      id={id}
      role="group"
      data-slot="checkbox-group"
      aria-labelledby={ariaLabelledBy ?? `${id}-label`}
      aria-describedby={ariaDescribedBy}
      aria-invalid={ariaInvalid || undefined}
      aria-required={ariaRequired}
      aria-disabled={disabled || undefined}
      className={cn(
        "grid gap-x-4 gap-y-2 rounded-lg border border-input px-2.5 py-2 text-sm aria-invalid:border-destructive aria-invalid:ring-3 aria-invalid:ring-destructive/20 aria-disabled:opacity-50",
        columns === 2 ? "grid-cols-2" : "grid-cols-1",
        className,
        /* A multi-row control: the FormField's control height is a minimum here, not a size. */
        "h-auto min-h-10",
      )}
    >
      {options.map((option) => {
        const optionId = `${id}-${option.id}`;
        return (
          <label key={option.id} htmlFor={optionId} className={cn("flex min-w-0 items-center gap-2", disabled ? "cursor-not-allowed" : "cursor-pointer")}>
            <Checkbox
              id={optionId}
              checked={value.includes(option.id)}
              onCheckedChange={(checked) => toggle(option.id, checked === true)}
              disabled={disabled}
              aria-invalid={ariaInvalid || undefined}
            />
            <span className="truncate">{option.label}</span>
          </label>
        );
      })}
    </div>
  );
}
