import { useMemo, useState, type FormEvent } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";
import { Plus } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { TotalsBlock } from "@/apps/transport/components/TotalsBlock";
import { RATE_LINE_KIND, RATE_LINE_KINDS, RATE_SIDE, useAddManualRateLine, useRemoveRateLine, useTripRateLines, useUpdateRateLine, type RateLineDto } from "@/domains/financial/rate-lines";
import { useLegsOfTrip } from "@/domains/planning-execution/legs";
import { useTransportTripSummary, type TransportTripDto } from "@/domains/planning-execution/trips";
import { getApiErrorMessage } from "@/lib/api/errors";
import { parseNumber } from "@/lib/forms/use-form-state";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useRecordUpdates } from "@/lib/realtime/use-record-updates";

/* The Cost and revenue tab of the trip editor (design 61): the cost rate lines per leg and the trip lines with their
 * provenance, a manual line, the totals, and the allocation per leg (allocated cost and revenue written by the
 * TripCalculator, design 59 Q1) with the CalculatingIndicator while a recalculation is pending (flow 2). */

export interface TripCostRevenueTabProps {
  trip: TransportTripDto;
  canEdit: boolean;
}

type Row = RateLineDto & { key: string; legLabel: string };

export function TripCostRevenueTab({ trip, canEdit }: TripCostRevenueTabProps) {
  const { t } = useL();
  const f = useFormat();
  const rateLines = useTripRateLines(trip.id);
  useRecordUpdates("Financial.TripRateLines", [trip.id], { queryKeysFor: () => [["financial", "rateLines", "trip", trip.id!.toLowerCase()]] });
  const legs = useLegsOfTrip(trip.id);
  const summary = useTransportTripSummary(trip.id);
  const update = useUpdateRateLine();
  const remove = useRemoveRateLine();
  const [adding, setAdding] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const currency = rateLines.data?.currencyCode ?? "";

  const legLabel = useMemo(() => {
    const byId = new Map((legs.data ?? []).map((leg) => [leg.id!.toLowerCase(), `${leg.orderNumber ?? ""} ${leg.origin?.city ?? ""} → ${leg.destination?.city ?? ""}`.trim()]));
    return (legId: string | null | undefined) => (legId ? (byId.get(legId.toLowerCase()) ?? legId) : t("PlanningExecution:Trip"));
  }, [legs.data, t]);

  const rows = useMemo<Row[]>(() => {
    const lines = [...(rateLines.data?.lines ?? [])];
    lines.sort((a, b) => (a.legId ? 0 : 1) - (b.legId ? 0 : 1) || (a.legId ?? "").localeCompare(b.legId ?? "") || (a.position ?? 0) - (b.position ?? 0));
    return lines.map((line) => ({ ...line, key: line.id ?? "", legLabel: legLabel(line.legId) }));
  }, [rateLines.data, legLabel]);

  const kindLabel = (kind: number | undefined) => t(RATE_LINE_KINDS.find((k) => k.value === kind)?.key ?? "");
  const columns = useMemo<ColDef<Row>[]>(
    () => [
      { field: "legLabel", headerName: t("PlanningExecution:Leg"), flex: 1, minWidth: 160 },
      {
        field: "kind",
        headerName: t("Financial:ValueKind"),
        minWidth: 140,
        cellRenderer: (p: { value: number | undefined; data: Row | undefined }) => (
          <Tooltip>
            <TooltipTrigger render={<Badge variant="secondary" />}>{kindLabel(p.value)}</TooltipTrigger>
            <TooltipContent>{[p.data?.priceListCode, p.data?.description, p.data?.isAdjusted ? t("Financial:Adjusted") : null].filter(Boolean).join(" · ") || kindLabel(p.value)}</TooltipContent>
          </Tooltip>
        ),
      },
      { colId: "description", headerName: t("Orders:Description"), flex: 1, minWidth: 160, valueGetter: (p) => p.data?.description || p.data?.priceListCode || p.data?.additionalServiceName || (p.data?.descriptionKey ? t(p.data.descriptionKey) : "") },
      { field: "quantity", headerName: t("Financial:Quantity"), width: 110, minWidth: 90, type: "rightAligned", cellClass: "tabular-nums", editable: (p) => canEdit && p.data?.isManual === true, valueFormatter: (p: ValueFormatterParams<Row, number>) => f.number(p.value, { maxDecimals: 4 }) },
      { field: "unitPrice", headerName: t("Financial:UnitPrice"), width: 120, minWidth: 100, type: "rightAligned", cellClass: "tabular-nums", editable: (p) => canEdit && p.data?.kind !== RATE_LINE_KIND.FixedPrice, valueFormatter: (p: ValueFormatterParams<Row, number>) => f.unitPrice(p.value) },
      { field: "amount", headerName: `${t("Financial:Amount")} (${currency})`, width: 130, minWidth: 110, type: "rightAligned", cellClass: "tabular-nums", valueFormatter: (p: ValueFormatterParams<Row, number>) => f.amount(p.value) },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, f, canEdit, currency],
  );

  const onRowsChange = (next: Row[]) => {
    setError(null);
    for (const row of rows.filter((row) => !next.some((r) => r.key === row.key))) {
      remove.mutateAsync(row.id!).then(() => toast.success(t("Financial:RateLineDeleted"))).catch((err: unknown) => setError(getApiErrorMessage(err, t)));
    }
    const changed = next.find((row) => {
      const before = rows.find((r) => r.key === row.key);
      return before && (before.quantity !== row.quantity || before.unitPrice !== row.unitPrice);
    });
    if (!changed) return;
    update
      .mutateAsync({ id: changed.id!, dto: { concurrencyStamp: changed.concurrencyStamp ?? "", unitPrice: Number(changed.unitPrice), quantity: changed.isManual ? Number(changed.quantity) : null, description: changed.description } })
      .then(() => toast.success(t("Financial:RateLineSaved")))
      .catch((err: unknown) => setError(getApiErrorMessage(err, t)));
  };

  const totals = rateLines.data?.totals;
  const state = rateLines.data?.state;

  return (
    <div className="flex flex-col gap-4">
      {error && (
        <Alert variant="destructive">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}
      <FormSection title={t("PlanningExecution:CostRateLines")}>
        <div className="h-64">
          <LazyLinesGrid<Row>
            rows={rows}
            onRowsChange={onRowsChange}
            getRowId={(row) => row.key}
            columnDefs={columns}
            readOnly={!canEdit}
            canRemoveRow={(row) => row.isManual === true}
            emptyText={t("Financial:NoRateLines")}
            toolbar={
              <Button type="button" size="sm" variant="outline" onClick={() => setAdding(true)}>
                <Plus /> {t("Financial:AddManualLine")}
              </Button>
            }
          />
        </div>
        <TotalsBlock
          className="mt-3"
          currencyCode={currency}
          state={state}
          lines={[
            { label: t("PlanningExecution:CostAmount"), value: totals?.total ?? trip.costAmount },
            { label: t("PlanningExecution:RevenueAmount"), value: trip.revenueAmount },
            { label: t("PlanningExecution:MarginAmount"), value: summary.data?.marginAmount ?? ((trip.revenueAmount ?? 0) - (trip.costAmount ?? 0)), grand: true },
          ]}
        />
      </FormSection>
      <FormSection title={t("PlanningExecution:AllocationPerLeg")}>
        {(legs.data ?? []).length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("PlanningExecution:NoLegs")}</p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t("PlanningExecution:Order")}</TableHead>
                <TableHead>{t("PlanningExecution:Origin")}</TableHead>
                <TableHead>{t("PlanningExecution:Destination")}</TableHead>
                <TableHead className="text-right">{`${t("PlanningExecution:Distance")} (km)`}</TableHead>
                <TableHead className="text-right">{t("PlanningExecution:AllocatedCost")}</TableHead>
                <TableHead className="text-right">{t("PlanningExecution:AllocatedRevenue")}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {(legs.data ?? []).map((leg) => (
                <TableRow key={leg.id}>
                  <TableCell>{leg.orderNumber}</TableCell>
                  <TableCell>{[leg.origin?.name, leg.origin?.city].filter(Boolean).join(", ")}</TableCell>
                  <TableCell>{[leg.destination?.name, leg.destination?.city].filter(Boolean).join(", ")}</TableCell>
                  <TableCell className="text-right tabular-nums">{leg.distanceM == null ? "" : f.number(leg.distanceM / 1000, { maxDecimals: 1 })}</TableCell>
                  <TableCell className="text-right tabular-nums">{f.amount(leg.allocatedCostAmount ?? 0)}</TableCell>
                  <TableCell className="text-right tabular-nums">{f.amount(leg.allocatedRevenueAmount ?? 0)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </FormSection>
      {adding && <ManualCostLineDialog tripId={trip.id!} currency={currency} legs={(legs.data ?? []).map((leg) => ({ id: leg.id!, label: legLabel(leg.id) }))} onClose={() => setAdding(false)} />}
    </div>
  );
}

function ManualCostLineDialog({ tripId, currency, legs, onClose }: { tripId: string; currency: string; legs: { id: string; label: string }[]; onClose: () => void }) {
  const { t } = useL();
  const add = useAddManualRateLine();
  const [description, setDescription] = useState("");
  const [quantity, setQuantity] = useState("1");
  const [unitPrice, setUnitPrice] = useState("");
  const [legId, setLegId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [submitted, setSubmitted] = useState(false);
  const price = parseNumber(unitPrice);
  const priceError = submitted && (price == null || Number.isNaN(price)) ? t("MasterData:Required") : undefined;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setSubmitted(true);
    if (price == null || Number.isNaN(price)) return;
    setError(null);
    const qty = parseNumber(quantity);
    try {
      await add.mutateAsync({ tripId, legId, side: RATE_SIDE.Cost, kind: RATE_LINE_KIND.Manual, description: description.trim() || null, quantity: qty == null || Number.isNaN(qty) ? 1 : qty, quantitySource: 0, unitPrice: price, currencyCode: currency || null });
      toast.success(t("Financial:RateLineSaved"));
      onClose();
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  return (
    <EntityDialog
      open
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="action"
      size="sm"
      title={t("Financial:AddManualLine")}
      isBusy={add.isPending}
      footer={
        <>
          <Button type="button" variant="outline" onClick={onClose} disabled={add.isPending}>
            {t("Cancel")}
          </Button>
          <Button type="submit" form="cost-line-form" disabled={add.isPending}>
            {add.isPending ? t("Saving") : t("Financial:AddLine")}
          </Button>
        </>
      }
    >
      <form id="cost-line-form" onSubmit={(e) => void submit(e)} noValidate className="grid grid-cols-1 gap-3">
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <FormField label={t("PlanningExecution:Leg")} htmlFor="cost-line-leg" floating="always">
          {/* DEVIATION(UI-LOOK-004): the trip's own legs: nothing to create in place, a leg is derived from the orders' lines and never created by hand. */}
          <ReferenceCombobox id="cost-line-leg" value={legId} onChange={setLegId} options={legs.map((leg) => ({ value: leg.id, label: leg.label }))} />
        </FormField>
        <FormField label={t("Orders:Description")} htmlFor="cost-line-description">
          <Input id="cost-line-description" value={description} maxLength={256} onChange={(e) => setDescription(e.target.value)} autoFocus />
        </FormField>
        <FormField label={t("Financial:Quantity")} htmlFor="cost-line-quantity">
          <Input id="cost-line-quantity" inputMode="decimal" className="tabular-nums" value={quantity} onChange={(e) => setQuantity(e.target.value)} />
        </FormField>
        <FormField label={`${t("Financial:UnitPrice")} (${currency})`} htmlFor="cost-line-price" required error={priceError}>
          <Input id="cost-line-price" inputMode="decimal" className="tabular-nums" value={unitPrice} onChange={(e) => setUnitPrice(e.target.value)} />
        </FormField>
      </form>
    </EntityDialog>
  );
}
