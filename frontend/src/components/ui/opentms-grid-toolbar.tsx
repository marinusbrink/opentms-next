import type { ReactNode } from "react";
import { Columns3, Download, RefreshCw, RotateCcw, Search } from "lucide-react";

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

/* The grid toolbar (UI-GRID-005): search, then the page's filter controls on the left;
 * Refresh, Export (when the design includes it), Columns and Reset on the right. */
interface OpenTmsGridToolbarProps<TRow> {
  searchValue: string;
  onSearchChange: (value: string) => void;
  columnDefs: ColDef<TRow>[];
  columnVisibility: Record<string, boolean>;
  onToggleColumn: (colId: string, visible: boolean) => void;
  onReset: () => void;
  onRefresh?: () => void;
  onExport?: () => void;
  filters?: ReactNode;
  className?: string;
}

interface IconActionProps {
  label: string;
  icon: ReactNode;
  onClick: () => void;
}

function IconAction({ label, icon, onClick }: IconActionProps) {
  return (
    <Tooltip>
      <TooltipTrigger
        render={<Button variant="outline" size="icon-sm" onClick={onClick} aria-label={label} />}
      >
        {icon}
      </TooltipTrigger>
      <TooltipContent>{label}</TooltipContent>
    </Tooltip>
  );
}

export function OpenTmsGridToolbar<TRow>({
  searchValue,
  onSearchChange,
  columnDefs,
  columnVisibility,
  onToggleColumn,
  onReset,
  onRefresh,
  onExport,
  filters,
  className,
}: OpenTmsGridToolbarProps<TRow>) {
  const { t } = useL();

  const toggleableColumns = columnDefs.filter(
    (col) => col.colId != null || col.field != null,
  );

  return (
    <TooltipProvider>
      <div className={cn("flex items-center gap-2 border-b bg-surface-page px-3 py-2", className)}>
        <div className="relative w-full max-w-sm">
          <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            className="pl-8"
            placeholder={t("Grid:Search")}
            aria-label={t("Grid:Search")}
            value={searchValue}
            onChange={(e) => onSearchChange(e.target.value)}
          />
        </div>

        {filters && (
          <div data-slot="grid-filters" className="flex items-center gap-2">
            {filters}
          </div>
        )}

        <div className="ml-auto flex items-center gap-2">
          {onRefresh && (
            <IconAction label={t("Grid:Refresh")} icon={<RefreshCw />} onClick={onRefresh} />
          )}
          {onExport && (
            <IconAction label={t("Grid:Export")} icon={<Download />} onClick={onExport} />
          )}

          <DropdownMenu>
            <DropdownMenuTrigger
              render={
                <Button variant="outline" size="sm">
                  <Columns3 />
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

          <IconAction label={t("Grid:Reset")} icon={<RotateCcw />} onClick={onReset} />
        </div>
      </div>
    </TooltipProvider>
  );
}
