import { useEffect, useMemo, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Trash2, TriangleAlert } from "lucide-react";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { Spinner } from "@/components/ui/spinner";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Textarea } from "@/components/ui/textarea";
import { defaultAddressIdOf } from "@/apps/transport/components/RelationFields";
import { EMPTY_END, itemFromEntry, type EditorMode, type EntryEnd, type EntryWaypoints, type LineValues } from "@/apps/transport/orders/order-form";
import { checkRow, matchByCodeOrName, parsePastedRows, problemCells, type CheckedRow, type Match, type PasteColumn } from "@/apps/transport/orders/paste-items";
import { relationAddressesQuery, relationLookupPage, transportUnits, type RelationOption } from "@/domains/master-data/entities";
import { getApiErrorMessage } from "@/lib/api/errors";
import { formattingLocale } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* Paste from Excel (design 61 "Order page, legacy tab structure" 3): the pasted rows become freight items after a preview that
 * marks the cells it could not match. The rows come from a paste in the freight item entry, or from this dialog's text
 * field (the Paste command). Relations are looked up on the server by the typed code or name, transport units in their
 * list; a matched relation takes its default (or only) address, as the stop strip does. A marked cell stays empty in the
 * new item and is completed in the item's detail; a row can be dropped before adding (a header row, a total). The
 * preview is a fixed, read-only table inside a dialog (UI-GRID-001 allows a Table there). */

type UnitOption = { id: string; code?: string | null; name?: string | null };
type Row = CheckedRow<RelationOption, UnitOption> & { key: number };

export interface PasteItemsDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Text pasted into the entry; empty when the dialog was opened from its command. */
  initialText: string;
  mode: EditorMode;
  /** The order date: the date of a row whose date cell is empty (as the stop strip starts on it). */
  orderDate: string | null;
  waypoints: EntryWaypoints;
  /** How many items the order can still take. */
  room: number;
  onAdd: (items: LineValues[]) => void;
}

export function PasteItemsDialog({ open, onOpenChange, initialText, mode, orderDate, waypoints, room, onAdd }: PasteItemsDialogProps) {
  const { t, culture } = useL();
  const queryClient = useQueryClient();
  const units = transportUnits.useLookup(open);
  /* The text being checked: the paste that opened the dialog, or one into its text field. */
  const [text, setText] = useState(initialText);
  const [draft, setDraft] = useState("");
  const [rows, setRows] = useState<Row[] | null>(null);
  const [checking, setChecking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);

  useEffect(() => {
    if (open) {
      setText(initialText);
      setDraft("");
      setRows(null);
      setError(null);
    }
  }, [open, initialText]);

  /* Checks the pasted rows once the text and the unit list are there. */
  const unitList = useMemo(() => (units.data ?? []).map((u) => ({ id: u.id ?? "", code: u.code, name: u.name })), [units.data]);
  useEffect(() => {
    if (!open || !text.trim() || units.data == null) return;
    let cancelled = false;
    const run = async () => {
      setChecking(true);
      setError(null);
      try {
        const pasted = parsePastedRows(text);
        const terms = [...new Set(pasted.flatMap((row) => [row.from.trim(), row.to.trim()]).filter(Boolean))];
        const found = new Map<string, Match<RelationOption>>();
        const page = relationLookupPage();
        await Promise.all(
          terms.map(async (term) => {
            const result = await page({ term, skip: 0, take: 25 });
            found.set(term.toLowerCase(), matchByCodeOrName(term, result.items));
          }),
        );
        const relationMatch = (cell: string): Match<RelationOption> => found.get(cell.trim().toLowerCase()) ?? { text: cell, value: null, status: cell.trim() ? "unmatched" : "empty" };
        const unitMatch = (cell: string) => matchByCodeOrName(cell, unitList);
        const locale = formattingLocale(culture);
        if (!cancelled) setRows(pasted.map((row, index) => ({ ...checkRow(row, mode, locale, relationMatch, unitMatch), key: index })));
      } catch (err) {
        if (!cancelled) setError(getApiErrorMessage(err, t));
      } finally {
        if (!cancelled) setChecking(false);
      }
    };
    void run();
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- t only formats the error; the check runs per paste
  }, [open, text, units.data, unitList, mode, culture]);

  const taken = (rows ?? []).slice(0, Math.max(room, 0));
  const marked = taken.reduce((count, row) => count + problemCells(row).length, 0);

  /** A matched relation with its default (or only) address and that address's city. */
  const endOf = async (match: Match<RelationOption>): Promise<EntryEnd> => {
    if (!match.value) return EMPTY_END;
    const addresses = await queryClient.fetchQuery(relationAddressesQuery(match.value.id));
    const addressId = defaultAddressIdOf(addresses);
    return { relation: match.value, addressId, city: addresses.find((a) => a.id === addressId)?.city ?? match.value.city ?? "" };
  };

  const add = async () => {
    setAdding(true);
    setError(null);
    try {
      const items = await Promise.all(
        taken.map(async (row) =>
          itemFromEntry(
            {
              from: await endOf(row.from),
              to: await endOf(row.to),
              date: row.date.value ?? (mode === "order" ? orderDate : null),
              dayOffset: row.dayOffset.ok ? (row.dayOffset.value ?? "0") : "0",
              time: row.time.value ?? "",
              transportUnitId: row.unit.value?.id ?? null,
              transportUnitName: row.unit.value?.name ?? "",
              quantity: row.quantity.ok ? (row.quantity.value ?? "") : "",
              weightKg: row.weight.ok ? (row.weight.value ?? "") : "",
              reference: row.reference,
            },
            waypoints,
          ),
        ),
      );
      onAdd(items);
      onOpenChange(false);
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    } finally {
      setAdding(false);
    }
  };

  const headers: { column: PasteColumn; label: string }[] = [
    { column: "from", label: t("Orders:FromRelation") },
    { column: "to", label: t("Orders:ToRelation") },
    { column: "date", label: mode === "order" ? t("Orders:Date") : t("Orders:DayOffset") },
    { column: "time", label: t("Orders:Time") },
    { column: "unit", label: t("Orders:TransportUnit") },
    { column: "quantity", label: t("Orders:Quantity") },
    { column: "weight", label: t("Orders:WeightKg") },
    { column: "reference", label: t("Orders:Reference") },
  ];
  const cell = (row: Row, column: PasteColumn) => {
    const problems = problemCells(row);
    const problem = problems.includes(column);
    const match = (m: Match<{ name?: string | null }>) => (m.value ? (m.value.name ?? m.text) : m.text);
    const content =
      column === "from" ? match(row.from) : column === "to" ? match(row.to) : column === "unit" ? match(row.unit)
      : column === "date" ? (mode === "order" ? row.date.text : row.dayOffset.text)
      : column === "time" ? (row.time.value ?? row.time.text) : column === "quantity" ? row.quantity.text : column === "weight" ? row.weight.text : row.reference;
    const ambiguous = (column === "from" && row.from.status === "ambiguous") || (column === "to" && row.to.status === "ambiguous") || (column === "unit" && row.unit.status === "ambiguous");
    const reason = problem ? t(ambiguous ? "Orders:PasteAmbiguous" : column === "from" || column === "to" || column === "unit" ? "Orders:PasteNotFound" : "Orders:PasteInvalid") : undefined;
    return (
      <TableCell key={column} data-column={column} data-problem={problem ? (ambiguous ? "ambiguous" : "unmatched") : undefined} title={reason} className={cn(problem && "text-destructive", (column === "quantity" || column === "weight") && "text-right tabular-nums")}>
        <span className="inline-flex items-center gap-1">
          {problem && <TriangleAlert aria-label={reason} className="size-4 shrink-0" />}
          {content}
        </span>
      </TableCell>
    );
  };

  return (
    <EntityDialog
      open={open}
      onOpenChange={onOpenChange}
      variant="action"
      size="lg"
      title={t("Orders:PasteFreightItems")}
      isBusy={adding}
      footer={
        <>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={adding}>
            {t("Cancel")}
          </Button>
          <Button type="button" onClick={() => void add()} disabled={adding || checking || taken.length === 0}>
            {adding ? t("Saving") : t("Orders:AddFreightItems").replace("{0}", String(taken.length))}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3" data-slot="paste-items">
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        {rows == null && draft.trim() && !checking && draft !== text && (
          <div>
            <Button type="button" size="sm" variant="outline" onClick={() => setText(draft)}>
              {t("Orders:PastePreview")}
            </Button>
          </div>
        )}
        {rows == null ? (
          <FormField label={t("Orders:PasteRows")} htmlFor="paste-items-text" hint={t("Orders:PasteRowsHint")} floating="always">
            <Textarea
              id="paste-items-text"
              rows={6}
              value={draft}
              autoFocus
              onChange={(e) => setDraft(e.target.value)}
              onPaste={(e) => {
                const pasted = e.clipboardData.getData("text/plain");
                if (!pasted.trim()) return;
                e.preventDefault();
                setDraft(pasted);
                setText(pasted);
              }}
            />
          </FormField>
        ) : (
          <>
            <Table data-slot="paste-preview">
              <TableHeader>
                <TableRow>
                  {headers.map((h) => (
                    <TableHead key={h.column}>{h.label}</TableHead>
                  ))}
                  <TableHead />
                </TableRow>
              </TableHeader>
              <TableBody>
                {taken.map((row) => (
                  <TableRow key={row.key} data-slot="paste-row">
                    {headers.map((h) => cell(row, h.column))}
                    <TableCell className="text-right">
                      <Button type="button" variant="ghost" size="icon-sm" aria-label={t("Remove")} title={t("Remove")} onClick={() => setRows((current) => (current ?? []).filter((r) => r.key !== row.key))}>
                        <Trash2 />
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            {(rows.length > taken.length || marked > 0) && (
              <Alert variant="warning" data-slot="paste-warning">
                <TriangleAlert aria-hidden="true" />
                <AlertDescription>
                  {marked > 0 && <p>{t("Orders:PasteMarkedHint").replace("{0}", String(marked))}</p>}
                  {rows.length > taken.length && <p>{t("Orders:PasteTooManyRows").replace("{0}", String(taken.length))}</p>}
                </AlertDescription>
              </Alert>
            )}
          </>
        )}
        {checking && (
          <div className="flex justify-center py-2">
            <Spinner aria-label={t("Loading")} />
          </div>
        )}
      </div>
    </EntityDialog>
  );
}
