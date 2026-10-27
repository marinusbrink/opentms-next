import { useRef, useState, type KeyboardEvent } from "react";
import { Combobox as ComboboxPrimitive } from "@base-ui/react";

import {
  Combobox,
  ComboboxContent,
  ComboboxCreateItem,
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
 * row of UI-LOOK-004 with the typed text, the same row as EntityLookup's: the list's last option.
 *
 * Keyboard (design 61 "Order page, legacy tab structure" 3, keyboard first): typing highlights the first match, Enter selects it,
 * Enter on a term nothing matches opens "Add new …" (then the only option), ArrowDown reaches "Add new …", Tab commits
 * the highlighted match and moves on (UI-KEY-004), Escape closes the list and never clears the value. */
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
  const typed = term.trim();
  /* Base UI's own contains-filter (collator based); the "Add new …" row always stays. */
  const { contains } = ComboboxPrimitive.useFilter({ multiple: true });
  const items = onCreateNew ? [...options, CREATE_OPTION] : options;
  const highlightedRef = useRef<ReferenceOption | undefined>(undefined);

  const openCreate = () => {
    if (!onCreateNew) return;
    setOpen(false);
    setTerm("");
    onCreateNew(typed);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement> & { preventBaseUIHandler: () => void }) => {
    /* UI-KEY-002: Escape closes the list and never clears the value (Base UI clears it on a closed list). */
    if (event.key === "Escape" && !open) event.preventBaseUIHandler();
    /* UI-KEY-004: Base UI closes on Tab without selecting; the highlighted match is handed over first. */
    if (event.key === "Tab" && open) {
      const match = highlightedRef.current;
      if (match && match !== CREATE_OPTION && match.value !== value) onChange(match.value);
    }
  };

  return (
    <Combobox
      items={items}
      value={selected}
      onValueChange={(next: ReferenceOption | null) => {
        if (next !== CREATE_OPTION) onChange(next?.value ?? null);
      }}
      isItemEqualToValue={(a: ReferenceOption, b: ReferenceOption) => a.value === b.value}
      filter={(item: ReferenceOption, query: string, toString?: (item: ReferenceOption) => string) => item === CREATE_OPTION || contains(item, query, toString)}
      /* The first match is highlighted while typing ("always" also re-highlights after the list was empty; Base UI
       * types only the boolean). */
      autoHighlight={(typed.length > 0 ? "always" : true) as boolean}
      onItemHighlighted={(item: ReferenceOption | undefined) => {
        highlightedRef.current = item;
      }}
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
        onKeyDown={onKeyDown}
        showClear={!required && selected != null && !disabled}
        className={className}
        aria-invalid={aria["aria-invalid"]}
        aria-describedby={aria["aria-describedby"]}
        aria-required={aria["aria-required"]}
      />
      <ComboboxContent>
        <ComboboxEmpty>{t("MasterData:NoOptions")}</ComboboxEmpty>
        <ComboboxList>
          {(item: ReferenceOption) =>
            item === CREATE_OPTION ? (
              <ComboboxCreateItem key={item.value} value={item} label={createLabel ?? t("AddNew")} onCreate={openCreate} />
            ) : (
              <ComboboxItem key={item.value} value={item}>
                {item.label}
              </ComboboxItem>
            )
          }
        </ComboboxList>
      </ComboboxContent>
    </Combobox>
  );
}

/** The "Add new …" row (UI-LOOK-004), the list's last option. */
const CREATE_OPTION: ReferenceOption = { value: "\u0000reference-combobox-create", label: "" };
