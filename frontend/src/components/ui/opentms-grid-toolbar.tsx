import { Columns2, RotateCcw, Search } from "lucide-react";

import type { ColDef } from "ag-grid-community";

import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

interface OpenTmsGridToolbarProps<TRow> {
  searchValue: string;
  onSearchChange: (value: string) => void;
  columnDefs: ColDef<TRow>[];
  columnVisibility: Record<string, boolean>;
  onToggleColumn: (colId: string, visible: boolean) => void;
  onReset: () => void;
  className?: string;
}

export function OpenTmsGridToolbar<TRow>({
  searchValue,
  onSearchChange,
  columnDefs,
  columnVisibility,
  onToggleColumn,
  onReset,
  className,
}: OpenTmsGridToolbarProps<TRow>) {
  const { t } = useL();

  const toggleableColumns = columnDefs.filter(
    (col) => col.colId != null || col.field != null,
  );

  return (
    <div className={cn("flex items-center gap-2 border-b px-3 py-2", className)}>
      <div className="relative max-w-sm flex-1">
        <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          className="pl-8"
          placeholder={t("Grid:Search")}
          value={searchValue}
          onChange={(e) => onSearchChange(e.target.value)}
        />
      </div>

      <DropdownMenu>
        <DropdownMenuTrigger
          render={
            <Button variant="outline" size="sm">
              <Columns2 />
              {t("Grid:Columns")}
            </Button>
          }
        />
        <DropdownMenuContent align="end">
          {toggleableColumns.map((col) => {
            const id = (col.colId ?? col.field) as string;
            const label = String(col.headerName ?? col.field ?? id);
            const isVisible = columnVisibility[id] ?? true;
            return (
              <DropdownMenuCheckboxItem
                key={id}
                checked={isVisible}
                onCheckedChange={(checked) => onToggleColumn(id, Boolean(checked))}
              >
                {label}
              </DropdownMenuCheckboxItem>
            );
          })}
        </DropdownMenuContent>
      </DropdownMenu>

      <TooltipProvider>
        <Tooltip>
          <TooltipTrigger
            render={
              <Button
                variant="outline"
                size="icon"
                onClick={onReset}
                aria-label={t("Grid:Reset")}
              >
                <RotateCcw />
              </Button>
            }
          />
          <TooltipContent>{t("Grid:Reset")}</TooltipContent>
        </Tooltip>
      </TooltipProvider>
    </div>
  );
}
