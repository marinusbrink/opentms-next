
import {
  Combobox,
  ComboboxContent,
  ComboboxEmpty,
  ComboboxInput,
  ComboboxItem,
  ComboboxList,
} from "@/components/ui/combobox";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* A single-value picker over a static list loaded once (UI-LOOK-001: countries, currencies,
 * time zones; more than 15 options, so a searchable Combobox instead of a Select). The value is
 * the option id; the input shows the label and filters client-side. Optional pickers are
 * clearable, required ones are not (UI-LOOK-006). */
export interface ReferenceOption {
  value: string;
  label: string;
}

export interface ReferenceComboboxProps {
  id: string;
  value: string | null;
  onChange: (value: string | null) => void;
  options: ReferenceOption[];
  required?: boolean;
  disabled?: boolean;
  "aria-invalid"?: boolean;
  "aria-describedby"?: string;
  "aria-required"?: boolean;
  placeholder?: string;
  className?: string;
}

export function ReferenceCombobox({
  id,
  value,
  onChange,
  options,
  required = false,
  disabled = false,
  placeholder,
  className,
  ...aria
}: ReferenceComboboxProps) {
  const { t } = useL();
  const selected = options.find((o) => o.value === value) ?? null;

  return (
    <Combobox
      items={options}
      value={selected}
      onValueChange={(next: ReferenceOption | null) => onChange(next?.value ?? null)}
      isItemEqualToValue={(a: ReferenceOption, b: ReferenceOption) => a.value === b.value}
      disabled={disabled}
    >
      <ComboboxInput
        id={id}
        placeholder={placeholder}
        disabled={disabled}
        showClear={!required && selected != null && !disabled}
        className={className}
        aria-invalid={aria["aria-invalid"]}
        aria-describedby={aria["aria-describedby"]}
        aria-required={aria["aria-required"]}
      />
      <ComboboxContent>
        <ComboboxEmpty>{t("MasterData:NoOptions")}</ComboboxEmpty>
        <ComboboxList>
          {(item: ReferenceOption) => (
            <ComboboxItem key={item.value} value={item}>
              {item.label}
            </ComboboxItem>
          )}
        </ComboboxList>
      </ComboboxContent>
    </Combobox>
  );
}
