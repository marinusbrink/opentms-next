import { useState } from "react";
import { Plus } from "lucide-react";

import { Button } from "@/components/ui/button";
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
 * clearable, required ones are not (UI-LOOK-006). Over records, `onCreateNew` adds the "Add new …"
 * row of UI-LOOK-004 with the typed text, the same row as EntityLookup's. */
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
  /** UI-LOOK-004: shows "Add new …" at the end of the list with the typed text; omit when the user may not create (or over a fixed list). */
  onCreateNew?: (term: string) => void;
  /** The label of that row, already translated, e.g. t("MasterData:AddNewCountry"). */
  createLabel?: string;
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
  onCreateNew,
  createLabel,
  ...aria
}: ReferenceComboboxProps) {
  const { t } = useL();
  const selected = options.find((o) => o.value === value) ?? null;
  const [open, setOpen] = useState(false);
  /* What the user typed since the list opened: the shown label of the selection is no search term. */
  const [term, setTerm] = useState("");

  return (
    <Combobox
      items={options}
      value={selected}
      onValueChange={(next: ReferenceOption | null) => onChange(next?.value ?? null)}
      isItemEqualToValue={(a: ReferenceOption, b: ReferenceOption) => a.value === b.value}
      disabled={disabled}
      open={open}
      onOpenChange={(next: boolean) => {
        setOpen(next);
        if (!next) setTerm("");
      }}
      onInputValueChange={(next: string, details: { reason: string }) => {
        if (details.reason === "input-change" || details.reason === "input-paste" || details.reason === "input-clear") setTerm(next);
      }}
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
        {onCreateNew && (
          <div className="border-t border-border p-1">
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="w-full justify-start"
              onClick={() => {
                const typed = term.trim();
                setOpen(false);
                setTerm("");
                onCreateNew(typed);
              }}
            >
              <Plus aria-hidden="true" /> {createLabel ?? t("AddNew")}
            </Button>
          </div>
        )}
      </ComboboxContent>
    </Combobox>
  );
}
