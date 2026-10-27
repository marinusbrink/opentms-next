import { useMemo, useState, type FormEvent, type ReactNode } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { toOptions } from "@/apps/transport/components/fields";
import { additionalServices } from "@/domains/financial/reference-data";
import {
  QUANTITY_SOURCES,
  RATE_LINE_KIND,
  RATE_LINE_KINDS,
  RATE_SIDE,
  useAddManualRateLine,
  useRemoveRateLine,
  useSetFixedPrice,
  useUpdateRateLine,
  type RateLineDto,
} from "@/domains/financial/rate-lines";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import { getApiErrorMessage } from "@/lib/api/errors";
import { parseNumber } from "@/lib/forms/use-form-state";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The stored rate lines on the order page (design 61 "Order page, legacy tab structure" 4, 5 and 6): one grid for a freight item's
 * lines (its Pricing tab in Edit freight item) and one for the order-level lines (the Financial tab: manual, fixed
 * price, D&S; the Additional services tab of General: additional services), with the kind badge and its provenance tooltip, quantity, unit price and amount. A calculated line's unit
 * price is adjusted inline and a manual line removed with the trash icon; each is its own command on the server
 * (design 57), run at once: these are stored lines, not form state. Reads never calculate (AD-07). */

export type RateLineRow = RateLineDto & { key: string };

export interface RateLinesGridProps {
  rows: RateLineDto[];
  currency: string;
  editable: boolean;
  toolbar?: ReactNode;
  /** A command's refusal, shown where the user is (UI-FB-004). */
  onError: (message: string | null) => void;
  heightClass?: string;
}

export function RateLinesGrid({ rows: lines, currency, editable, toolbar, onError, heightClass = "h-48" }: RateLinesGridProps) {
  const { t } = useL();
  const f = useFormat();
  const update = useUpdateRateLine();
  const remove = useRemoveRateLine();
  const rows = useMemo<RateLineRow[]>(() => [...lines].sort((a, b) => (a.position ?? 0) - (b.position ?? 0)).map((line) => ({ ...line, key: line.id ?? "" })), [lines]);

  const kindLabel = (kind: number | undefined) => t(RATE_LINE_KINDS.find((k) => k.value === kind)?.key ?? "");
  const descriptionOf = (row: RateLineDto) => row.description || row.priceListCode || row.discountOrSurchargeName || row.additionalServiceName || (row.descriptionKey ? t(row.descriptionKey) : "");
  const provenance = (row: RateLineDto) =>
    [
      row.priceListCode ? `${t("Financial:PriceList")}: ${row.priceListCode}` : null,
      row.priceListRuleId ? `${t("Financial:Rule")}: ${row.priceListRuleId.slice(0, 8)}` : null,
      row.columnPosition != null ? `${t("Financial:Columns")}: ${row.columnPosition}` : null,
      row.quantitySource != null ? `${t("Financial:Quantity")}: ${t(QUANTITY_SOURCES.find((q) => q.value === row.quantitySource)?.key ?? "")}` : null,
      row.isAdjusted ? t("Financial:Adjusted") : null,
    ]
      .filter(Boolean)
      .join(" · ");

  const columns = useMemo<ColDef<RateLineRow>[]>(
    () => [
      {
        field: "kind",
        headerName: t("Financial:ValueKind"),
        minWidth: 130,
        flex: 1,
        cellRenderer: (p: { value: number | undefined; data: RateLineRow | undefined }) => (
          <Tooltip>
            <TooltipTrigger render={<Badge variant="secondary" />}>{kindLabel(p.value)}</TooltipTrigger>
            <TooltipContent>{p.data ? provenance(p.data) || kindLabel(p.value) : ""}</TooltipContent>
          </Tooltip>
        ),
      },
      { colId: "description", headerName: t("Orders:Description"), flex: 2, minWidth: 180, valueGetter: (p) => (p.data ? descriptionOf(p.data) : "") },
      { field: "quantity", headerName: t("Financial:Quantity"), flex: 1, minWidth: 90, type: "rightAligned", cellClass: "tabular-nums", editable: (p) => editable && p.data?.isManual === true, valueFormatter: (p: ValueFormatterParams<RateLineRow, number>) => f.number(p.value, { maxDecimals: 4 }) },
      { field: "unitPrice", headerName: t("Financial:UnitPrice"), flex: 1, minWidth: 100, type: "rightAligned", cellClass: "tabular-nums", editable: (p) => editable && p.data?.kind !== RATE_LINE_KIND.FixedPrice, valueFormatter: (p: ValueFormatterParams<RateLineRow, number>) => f.unitPrice(p.value) },
      { field: "amount", headerName: `${t("Financial:Amount")} (${currency})`, flex: 1, minWidth: 110, type: "rightAligned", cellClass: "tabular-nums", valueFormatter: (p: ValueFormatterParams<RateLineRow, number>) => f.amount(p.value) },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, f, editable, currency],
  );

  const onRowsChange = (next: RateLineRow[]) => {
    onError(null);
    for (const row of rows.filter((row) => !next.some((r) => r.key === row.key))) {
      remove
        .mutateAsync(row.id!)
        .then(() => toast.success(t("Financial:RateLineDeleted")))
        .catch((err: unknown) => onError(getApiErrorMessage(err, t)));
    }
    const changed = next.find((row) => {
      const before = rows.find((r) => r.key === row.key);
      return before && (before.quantity !== row.quantity || before.unitPrice !== row.unitPrice);
    });
    if (!changed) return;
    update
      .mutateAsync({ id: changed.id!, dto: { concurrencyStamp: changed.concurrencyStamp ?? "", unitPrice: Number(changed.unitPrice), quantity: changed.isManual ? Number(changed.quantity) : null, description: changed.description } })
      .then(() => toast.success(t("Financial:RateLineSaved")))
      .catch((err: unknown) => onError(getApiErrorMessage(err, t)));
  };

  return (
    <div className={heightClass}>
      <LazyLinesGrid<RateLineRow>
        rows={rows}
        onRowsChange={onRowsChange}
        getRowId={(row) => row.key}
        columnDefs={columns}
        readOnly={!editable}
        canRemoveRow={(row) => row.isManual === true}
        emptyText={t("Financial:NoRateLines")}
        toolbar={toolbar}
      />
    </div>
  );
}

export interface RateLineDialogProps {
  kind: "manual" | "service" | "fixed";
  orderId: string;
  /** The freight item the line belongs to; null for an order-level line. */
  orderLineId: string | null;
  currency: string;
  onClose: () => void;
}

/** Add manual line / Add additional service / Set fixed price: one action dialog (UI-DLG-002, UI-ACT-003, UI-KEY-001).
 * Opened from a freight item's Pricing tab the line belongs to that item; from the Financial or Additional services
 * tab to the order. */
export function RateLineDialog({ kind, orderId, orderLineId, currency, onClose }: RateLineDialogProps) {
  const { t } = useL();
  const add = useAddManualRateLine();
  const setFixed = useSetFixedPrice();
  const services = additionalServices.useLookup(kind === "service");
  const serviceOptions = useMemo(() => toOptions(services.data), [services.data]);
  const [description, setDescription] = useState("");
  const [quantity, setQuantity] = useState("1");
  const [unitPrice, setUnitPrice] = useState("");
  const [serviceId, setServiceId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [submitted, setSubmitted] = useState(false);
  const busy = add.isPending || setFixed.isPending;
  const inPlace = useCreateInPlace();

  const title = kind === "manual" ? t("Financial:AddManualLine") : kind === "service" ? t("Financial:AddAdditionalService") : t("Financial:SetFixedPrice");
  const verb = kind === "fixed" ? t("Financial:SetFixedPrice") : t("Financial:AddLine");
  const price = parseNumber(unitPrice);
  const qty = parseNumber(quantity);
  const priceError = submitted && (price == null || Number.isNaN(price)) ? t("MasterData:Required") : undefined;
  const serviceError = submitted && kind === "service" && !serviceId ? t("MasterData:Required") : undefined;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setSubmitted(true);
    setError(null);
    if (price == null || Number.isNaN(price) || (kind === "service" && !serviceId)) return;
    try {
      if (kind === "fixed") {
        await setFixed.mutateAsync({ orderId, amount: price, currencyCode: currency });
      } else {
        await add.mutateAsync({
          orderId,
          orderLineId,
          side: RATE_SIDE.Revenue,
          kind: kind === "service" ? RATE_LINE_KIND.AdditionalService : RATE_LINE_KIND.Manual,
          additionalServiceId: kind === "service" ? serviceId : null,
          description: description.trim() || null,
          quantity: qty == null || Number.isNaN(qty) ? 1 : qty,
          quantitySource: 0,
          unitPrice: price,
          currencyCode: currency,
        });
      }
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
      title={title}
      isBusy={busy}
      footer={
        <>
          <Button type="button" variant="outline" onClick={onClose} disabled={busy}>
            {t("Cancel")}
          </Button>
          <Button type="submit" form="rate-line-form" disabled={busy}>
            {busy ? t("Saving") : verb}
          </Button>
        </>
      }
    >
      <form id="rate-line-form" onSubmit={(e) => void submit(e)} noValidate className="grid grid-cols-1 gap-3">
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        {kind === "service" && (
          <FormField label={t("Financial:AdditionalService")} htmlFor="rate-line-service" required error={serviceError} floating="always">
            <ReferenceCombobox
              id="rate-line-service"
              value={serviceId}
              onChange={setServiceId}
              options={serviceOptions}
              required
              aria-invalid={serviceError != null}
              {...inPlace.offer("AdditionalService", busy, (r) => setServiceId(r.id))}
            />
          </FormField>
        )}
        {kind === "manual" && (
          <FormField label={t("Orders:Description")} htmlFor="rate-line-description">
            <Input id="rate-line-description" value={description} maxLength={256} onChange={(e) => setDescription(e.target.value)} />
          </FormField>
        )}
        {kind !== "fixed" && (
          <FormField label={t("Financial:Quantity")} htmlFor="rate-line-quantity">
            <Input id="rate-line-quantity" inputMode="decimal" className="tabular-nums" value={quantity} onChange={(e) => setQuantity(e.target.value)} />
          </FormField>
        )}
        <FormField label={`${kind === "fixed" ? t("Financial:FixedPrice") : t("Financial:UnitPrice")} (${currency})`} htmlFor="rate-line-price" required error={priceError}>
          <Input id="rate-line-price" inputMode="decimal" className="tabular-nums" value={unitPrice} onChange={(e) => setUnitPrice(e.target.value)} autoFocus />
        </FormField>
      </form>
      {inPlace.dialog}
    </EntityDialog>
  );
}
