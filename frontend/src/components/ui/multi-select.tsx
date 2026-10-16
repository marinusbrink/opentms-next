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
  ComboboxTrigger,
} from "@/components/ui/combobox";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* The library multi-select (component map §3, "build"; UI-LOOK-007): the closed control shows the
 * selected names as a comma list truncated with "+N", never chips; the open list has a search box
 * and one checkable row per option. Built on the shadcn Combobox (Base UI) in multiple mode. */
export interface MultiSelectOption {
  id: string;
  label: string;
}

export interface MultiSelectProps {
  id: string;
  value: string[];
  onChange: (value: string[]) => void;
  options: MultiSelectOption[];
  disabled?: boolean;
  "aria-invalid"?: boolean;
  "aria-describedby"?: string;
  "aria-required"?: boolean;
  /** How many names show before the rest collapses into "+N". */
  maxVisible?: number;
  /** UI-LOOK-004: shows an "Add new …" row that opens the entity's editor with the typed term; omit without the Create permission. */
  onCreateNew?: (term: string) => void;
  /** The label of that row, already translated. */
  createLabel?: string;
  className?: string;
}

export function MultiSelect({
  id,
  value,
  onChange,
  options,
  disabled = false,
  maxVisible = 3,
  onCreateNew,
  createLabel,
  className,
  ...aria
}: MultiSelectProps) {
  const { t } = useL();
  const [term, setTerm] = useState("");
  const [open, setOpen] = useState(false);
  const selected = options.filter((o) => value.includes(o.id));
  const visible = selected.slice(0, maxVisible).map((o) => o.label);
  const rest = selected.length - visible.length;
  const summary =
    selected.length === 0
      ? t("MasterData:Select")
      : rest > 0
        ? `${visible.join(", ")}, ${t("MasterData:MoreSelected").replace("{0}", String(rest))}`
        : visible.join(", ");

  return (
    <Combobox
      multiple
      items={options}
      value={selected}
      onValueChange={(next: MultiSelectOption[]) => onChange(next.map((o) => o.id))}
      isItemEqualToValue={(a: MultiSelectOption, b: MultiSelectOption) => a.id === b.id}
      disabled={disabled}
      inputValue={term}
      onInputValueChange={(next: string) => setTerm(next)}
      open={open}
      onOpenChange={(next: boolean) => setOpen(next)}
    >
      <ComboboxTrigger
        id={id}
        disabled={disabled}
        aria-invalid={aria["aria-invalid"]}
        aria-describedby={aria["aria-describedby"]}
        aria-required={aria["aria-required"]}
        render={
          <Button
            type="button"
            variant="outline"
            className={cn(
              "h-10 w-full justify-between bg-background px-3 font-normal",
              selected.length === 0 && "text-muted-foreground",
              className,
            )}
          />
        }
      >
        {/* ComboboxTrigger renders the chevron itself. */}
        <span className="truncate text-left">{summary}</span>
      </ComboboxTrigger>
      <ComboboxContent>
        <ComboboxInput placeholder={t("MasterData:Search")} showTrigger={false} />
        <ComboboxEmpty>{t("MasterData:NoOptions")}</ComboboxEmpty>
        <ComboboxList>
          {(item: MultiSelectOption) => (
            <ComboboxItem key={item.id} value={item}>
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
                /* The editor opens on top: the list closes first (UI-LOOK-004). */
                const typed = term.trim();
                setOpen(false);
                setTerm("");
                onCreateNew(typed);
              }}
            >
              <Plus aria-hidden="true" /> {createLabel ?? t("MasterData:AddNew")}
            </Button>
          </div>
        )}
      </ComboboxContent>
    </Combobox>
  );
}
