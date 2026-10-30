import { useEffect, useId, useState, type FormEvent } from "react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import type { GridExportColumn } from "@/components/ui/opentms-grid-export";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The export dialog of a list (UI-GRID-013): an action dialog (size sm) with the two choices of the rule, "visible
 * columns or all columns" and "filtered rows or all rows", that hands the chosen columns (id and shown header, in
 * display order) and the row choice to the page's server export. The page downloads the file; a refusal of the server
 * (too many rows, an unknown column) shows inside the dialog, which stays open (UI-FB-004). There is no client-side
 * file: the server writes the xlsx. */

export interface GridExportChoice {
  /** The columns to export, in display order, with the header the user sees. */
  columns: { field: string; header: string }[];
  /** True: every row of the list, ignoring filters and search; false: the rows the list currently shows. */
  allRows: boolean;
}

export interface GridExportDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The grid's columns in display order (from OpenTmsGrid's onExport context). */
  columns: GridExportColumn[];
  /** Runs the server export and saves the file; a thrown error is shown in the dialog. */
  onExport: (choice: GridExportChoice) => Promise<void>;
}

type ColumnChoice = "visible" | "all";
type RowChoice = "filtered" | "all";

export function GridExportDialog({ open, onOpenChange, columns, onExport }: GridExportDialogProps) {
  const { t } = useL();
  const formId = useId();
  const [columnChoice, setColumnChoice] = useState<ColumnChoice>("visible");
  const [rowChoice, setRowChoice] = useState<RowChoice>("filtered");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setColumnChoice("visible");
      setRowChoice("filtered");
      setError(null);
      setBusy(false);
    }
  }, [open]);

  const visible = columns.filter((c) => c.visible);
  const chosen = columnChoice === "visible" ? visible : columns;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (chosen.length === 0) return;
    setError(null);
    setBusy(true);
    try {
      await onExport({ columns: chosen.map((c) => ({ field: c.id, header: c.header })), allRows: rowChoice === "all" });
      setBusy(false);
      onOpenChange(false);
      toast.success(t("Grid:ExportDownloaded"));
    } catch (err) {
      setBusy(false);
      setError(getApiErrorMessage(err, t));
    }
  };

  return (
    <EntityDialog
      open={open}
      onOpenChange={onOpenChange}
      variant="action"
      size="sm"
      title={t("Grid:ExportTitle")}
      isBusy={busy}
      footer={
        <>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={busy}>
            {t("Cancel")}
          </Button>
          <Button type="submit" form={formId} disabled={busy || chosen.length === 0}>
            {busy ? t("Grid:Exporting") : t("Grid:Export")}
          </Button>
        </>
      }
    >
      <form id={formId} data-slot="grid-export-form" onSubmit={(e) => void submit(e)} noValidate className="grid grid-cols-1 gap-4">
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <fieldset className="grid gap-2">
          <legend className="mb-2 text-label text-muted-foreground">{t("Grid:ExportColumns")}</legend>
          <RadioGroup value={columnChoice} onValueChange={(value) => setColumnChoice(value as ColumnChoice)} disabled={busy}>
            <label className="flex items-center gap-2 text-sm">
              <RadioGroupItem value="visible" />
              {t("Grid:ExportVisibleColumns")} ({visible.length})
            </label>
            <label className="flex items-center gap-2 text-sm">
              <RadioGroupItem value="all" />
              {t("Grid:ExportAllColumns")} ({columns.length})
            </label>
          </RadioGroup>
        </fieldset>
        <fieldset className="grid gap-2">
          <legend className="mb-2 text-label text-muted-foreground">{t("Grid:ExportRows")}</legend>
          <RadioGroup value={rowChoice} onValueChange={(value) => setRowChoice(value as RowChoice)} disabled={busy}>
            <label className="flex items-center gap-2 text-sm">
              <RadioGroupItem value="filtered" />
              {t("Grid:ExportFilteredRows")}
            </label>
            <label className="flex items-center gap-2 text-sm">
              <RadioGroupItem value="all" />
              {t("Grid:ExportAllRows")}
            </label>
          </RadioGroup>
        </fieldset>
      </form>
    </EntityDialog>
  );
}
