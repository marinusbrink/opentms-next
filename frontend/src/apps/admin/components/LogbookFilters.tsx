import { ChevronDown } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  changeTypeKey,
  LOGBOOK_CHANGE_TYPES,
  LOGBOOK_PERIODS,
  periodKey,
  type LogbookChangeType,
  type LogbookPeriod,
} from "@/domains/platform/administration-logbook";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The explicit filter controls of the logbook grids (UI-GRID-005): they render in the grid
 * toolbar's `filters` slot, next to the search box. Each is a small labelled choice. */

export interface ChoiceFilterProps<T extends string> {
  label: string;
  value: T;
  options: readonly T[];
  optionLabel: (value: T) => string;
  onChange: (value: T) => void;
}

export function ChoiceFilter<T extends string>({ label, value, options, optionLabel, onChange }: ChoiceFilterProps<T>) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={
          <Button variant="outline" size="sm" aria-label={label}>
            <span className="text-muted-foreground">{label}:</span>
            <span>{optionLabel(value)}</span>
            <ChevronDown aria-hidden="true" />
          </Button>
        }
      />
      <DropdownMenuContent align="start">
        <DropdownMenuRadioGroup value={value} onValueChange={(next) => onChange(next as T)}>
          {options.map((option) => (
            <DropdownMenuRadioItem key={option} value={option}>
              {optionLabel(option)}
            </DropdownMenuRadioItem>
          ))}
        </DropdownMenuRadioGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

export function PeriodFilter({ value, onChange }: { value: LogbookPeriod; onChange: (value: LogbookPeriod) => void }) {
  const { t } = useL();
  return (
    <ChoiceFilter
      label={t("Logbook:Period")}
      value={value}
      options={LOGBOOK_PERIODS}
      optionLabel={(period) => t(periodKey(period))}
      onChange={onChange}
    />
  );
}

const ALL_CHANGE_TYPES = "all" as const;
type ChangeTypeChoice = LogbookChangeType | typeof ALL_CHANGE_TYPES;
const CHANGE_TYPE_CHOICES: readonly ChangeTypeChoice[] = [ALL_CHANGE_TYPES, ...LOGBOOK_CHANGE_TYPES];

export function ChangeTypeFilter({
  value,
  onChange,
}: {
  value: LogbookChangeType | undefined;
  onChange: (value: LogbookChangeType | undefined) => void;
}) {
  const { t } = useL();
  return (
    <ChoiceFilter<ChangeTypeChoice>
      label={t("Logbook:ChangeType")}
      value={value ?? ALL_CHANGE_TYPES}
      options={CHANGE_TYPE_CHOICES}
      optionLabel={(choice) => (choice === ALL_CHANGE_TYPES ? t("Logbook:AllChangeTypes") : t(changeTypeKey(choice)))}
      onChange={(choice) => onChange(choice === ALL_CHANGE_TYPES ? undefined : choice)}
    />
  );
}

export function ErrorsOnlyFilter({ value, onChange }: { value: boolean; onChange: (value: boolean) => void }) {
  const { t } = useL();
  return (
    <label className="flex items-center gap-2 whitespace-nowrap text-sm">
      <Checkbox checked={value} onCheckedChange={(checked) => onChange(checked === true)} />
      {t("Logbook:ErrorsOnly")}
    </label>
  );
}
