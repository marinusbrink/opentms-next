import { useMemo, useState, type FormEvent } from "react";
import { Plus } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { TotalsBlock } from "@/apps/transport/components/TotalsBlock";
import type { LineValues } from "@/apps/transport/orders/order-form";
import { RateLinesGrid } from "@/apps/transport/orders/RateLines";
import { REVENUE_BASES } from "@/domains/financial/price-lists";
import { QUANTITY_SOURCES, RATE_LINE_KIND, RATE_SIDE, useAddManualRateLine, useOrderCostLineUpdates, useOrderCostLines, type QuantitySource } from "@/domains/financial/rate-lines";
import type { LegDto } from "@/domains/planning-execution/legs";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { parseNumber } from "@/lib/forms/use-form-state";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Purchase prices of a freight item (design 61 "Order page review" 4, legacy "Inkooptarieven"): under the sales table of
 * the Pricing tab in Edit freight item, the cost lines of this item on all its trips (Financial's cost read of the order,
 * filtered on the item: `legOrderLineId`), with the kind badge and provenance tooltip, the trip as a link, description,
 * basis, quantity, unit price and amount, and the total. Actions as on the sales side, each its own command at once:
 * Add purchase line (anchored to the item's first leg on the chosen trip), Adjust unit price inline (calculated lines),
 * Remove (manual lines). No purchase line before the item is on a trip (PO 2026-10-28): the section says so. Gated on
 * Financial.RateLines (view) and Financial.RateLines.Update (the actions, via `editable`). */

export interface ItemPurchasePricesProps {
  orderId: string;
  /** The stored freight item. */
  line: LineValues & { id: string };
  /** The order's legs (the item's are filtered here). */
  legs: LegDto[] | undefined;
  /** Update permission, an editable order status and an editable form. */
  editable: boolean;
}

interface ItemTrip {
  id: string;
  number: string;
  /** The item's first leg on the trip: the anchor of a new purchase line. */
  firstLegId: string;
}

/** The trips the item's legs are on, in leg order, each with the item's first leg on it. */
function tripsOfItem(legs: LegDto[] | undefined, lineId: string): ItemTrip[] {
  const trips = new Map<string, ItemTrip>();
  const own = (legs ?? []).filter((leg) => leg.orderLineId?.toLowerCase() === lineId.toLowerCase() && leg.tripId && leg.id).sort((a, b) => (a.position ?? 0) - (b.position ?? 0));
  for (const leg of own) {
    const key = leg.tripId!.toLowerCase();
    if (!trips.has(key)) trips.set(key, { id: leg.tripId!, number: leg.tripNumber ?? "", firstLegId: leg.id! });
  }
  return [...trips.values()];
}

/** The item's value for a basis, the quantity a new purchase line starts with (typed bases, per line and vehicle type: 1). */
function itemQuantityOf(line: LineValues, legs: LegDto[] | undefined, basis: QuantitySource): number | null {
  const n = (text: string) => {
    const value = parseNumber(text);
    return value == null || Number.isNaN(value) ? null : value;
  };
  const own = (legs ?? []).filter((leg) => line.id && leg.orderLineId?.toLowerCase() === line.id.toLowerCase());
  const legSum = (pick: (leg: LegDto) => number | null | undefined) => (own.some((leg) => pick(leg) != null) ? own.reduce((sum, leg) => sum + (pick(leg) ?? 0), 0) : null);
  switch (basis) {
    case 1:
      return n(line.quantity);
    case 2:
      return n(line.positions);
    case 3:
      return n(line.colli);
    case 4:
      return n(line.weightKg);
    case 5:
      return n(line.volumeM3);
    case 6:
      return n(line.loadingMeters);
    case 7:
      return n(line.loadFactor);
    case 8:
      return n(line.epq);
    case 9: {
      const m = line.distanceM ?? legSum((leg) => leg.distanceM);
      return m == null ? null : Math.round(m / 100) / 10;
    }
    case 10: {
      const m = legSum((leg) => leg.tollDistanceM);
      return m == null ? null : Math.round(m / 100) / 10;
    }
    case 11: {
      const s = line.durationS ?? legSum((leg) => leg.durationS);
      return s == null ? null : Math.round(s / 36) / 100;
    }
    default:
      return 1;
  }
}

export function ItemPurchasePrices({ orderId, line, legs, editable }: ItemPurchasePricesProps) {
  const { t } = useL();
  const canView = useIsGranted("Financial.RateLines");
  const trips = useMemo(() => tripsOfItem(legs, line.id), [legs, line.id]);
  const onTrip = trips.length > 0;
  const costLines = useOrderCostLines(orderId, canView && onTrip);
  useOrderCostLineUpdates(orderId, canView ? trips.map((trip) => trip.id) : []);
  const [adding, setAdding] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const id = line.id.toLowerCase();
  const rows = useMemo(
    () => (costLines.data?.lines ?? []).filter((r) => r.legOrderLineId?.toLowerCase() === id).sort((a, b) => (a.tripNumber ?? "").localeCompare(b.tripNumber ?? "") || (a.position ?? 0) - (b.position ?? 0)),
    [costLines.data, id],
  );
  const total = rows.reduce((sum, r) => sum + (r.amount ?? 0), 0);
  const currency = costLines.data?.currencyCode ?? "";

  if (!canView) return null;

  return (
    <FormSection title={t("Financial:PurchasePrices")}>
      {!onTrip ? (
        <p className="text-label text-muted-foreground" data-slot="purchase-prices-after-trip">
          {t("Financial:PurchasePricesAfterTrip")}
        </p>
      ) : (
        <div className="flex flex-col gap-3" data-slot="item-purchase-prices">
          {error && (
            <Alert variant="destructive">
              <AlertDescription>{error}</AlertDescription>
            </Alert>
          )}
          {costLines.error != null && (
            <Alert variant="destructive">
              <AlertDescription>{getApiErrorMessage(costLines.error, t)}</AlertDescription>
            </Alert>
          )}
          <RateLinesGrid
            rows={rows}
            currency={currency}
            editable={editable}
            onError={setError}
            withTripAndBasis
            toolbar={
              <Button type="button" size="sm" variant="outline" onClick={() => setAdding(true)}>
                <Plus /> {t("Financial:AddPurchaseLine")}
              </Button>
            }
          />
          <TotalsBlock lines={[{ label: t("Financial:PurchaseTotal"), value: costLines.data ? total : null, grand: true }]} currencyCode={currency} state={costLines.data?.state} />
        </div>
      )}
      {adding && <PurchaseLineDialog line={line} legs={legs} trips={trips} currency={currency} onClose={() => setAdding(false)} />}
    </FormSection>
  );
}

/** The bases a purchase line can have: typed, then the revenue bases in the legacy order (the trip distance is the
 *  own fleet's trip line, not an item's). */
const PURCHASE_BASES: { value: QuantitySource; key: string }[] = [QUANTITY_SOURCES[0], ...REVENUE_BASES.map((b) => ({ value: b.value, key: QUANTITY_SOURCES.find((q) => q.value === b.value)!.key }))];

interface PurchaseLineDialogProps {
  line: LineValues & { id: string };
  legs: LegDto[] | undefined;
  trips: ItemTrip[];
  currency: string;
  onClose: () => void;
}

/** Add purchase line: an action dialog (UI-DLG-002, UI-ACT-003, UI-KEY-001); the trip is preselected when there is one. */
function PurchaseLineDialog({ line, legs, trips, currency, onClose }: PurchaseLineDialogProps) {
  const { t } = useL();
  const add = useAddManualRateLine();
  const quantityText = (basis: QuantitySource) => {
    const value = itemQuantityOf(line, legs, basis);
    return value == null ? "" : String(value);
  };
  const [tripId, setTripId] = useState<string | null>(trips.length === 1 ? trips[0].id : null);
  const [description, setDescription] = useState("");
  const [basis, setBasis] = useState<QuantitySource>(1);
  const [quantity, setQuantity] = useState(() => quantityText(1));
  const [unitPrice, setUnitPrice] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitted, setSubmitted] = useState(false);
  const busy = add.isPending;
  const price = parseNumber(unitPrice);
  const qty = parseNumber(quantity);
  const trip = trips.find((x) => x.id === tripId);
  const tripError = submitted && !trip ? t("MasterData:Required") : undefined;
  const priceError = submitted && (price == null || Number.isNaN(price)) ? t("MasterData:Required") : undefined;
  const quantityError = submitted && qty != null && Number.isNaN(qty) ? t("MasterData:InvalidNumber") : undefined;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setSubmitted(true);
    setError(null);
    if (!trip || price == null || Number.isNaN(price) || (qty != null && Number.isNaN(qty))) return;
    try {
      await add.mutateAsync({
        tripId: trip.id,
        orderLineId: line.id,
        legId: trip.firstLegId,
        side: RATE_SIDE.Cost,
        kind: RATE_LINE_KIND.Manual,
        description: description.trim() || null,
        quantitySource: basis,
        quantity: qty ?? 1,
        unitPrice: price,
        /* The trip's cost currency (the server takes the trip's lines' currency when none is given). */
        currencyCode: currency || null,
      });
      toast.success(t("Financial:RateLineSaved"));
      onClose();
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  const basisItems = PURCHASE_BASES.map((b) => ({ value: String(b.value), label: t(b.key) }));
  return (
    <EntityDialog
      open
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="action"
      size="sm"
      title={t("Financial:AddPurchaseLine")}
      isBusy={busy}
      footer={
        <>
          <Button type="button" variant="outline" onClick={onClose} disabled={busy}>
            {t("Cancel")}
          </Button>
          <Button type="submit" form="purchase-line-form" disabled={busy}>
            {busy ? t("Saving") : t("Financial:AddLine")}
          </Button>
        </>
      }
    >
      <form id="purchase-line-form" onSubmit={(e) => void submit(e)} noValidate className="grid grid-cols-1 gap-3">
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <FormField label={t("Orders:Description")} htmlFor="purchase-line-description">
          <Input id="purchase-line-description" value={description} maxLength={256} onChange={(e) => setDescription(e.target.value)} autoFocus />
        </FormField>
        <FormField label={t("Financial:Basis")} htmlFor="purchase-line-basis" floating="always">
          <Select
            items={basisItems}
            value={String(basis)}
            onValueChange={(next) => {
              const value = Number(next) as QuantitySource;
              setBasis(value);
              setQuantity(quantityText(value));
            }}
          >
            <SelectTrigger id="purchase-line-basis" className="h-10 w-full bg-background">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {basisItems.map((item) => (
                <SelectItem key={item.value} value={item.value}>
                  {item.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </FormField>
        <FormField label={t("Financial:Quantity")} htmlFor="purchase-line-quantity" error={quantityError}>
          <Input id="purchase-line-quantity" inputMode="decimal" className="tabular-nums" value={quantity} onChange={(e) => setQuantity(e.target.value)} aria-invalid={quantityError != null} />
        </FormField>
        <FormField label={currency ? `${t("Financial:UnitPrice")} (${currency})` : t("Financial:UnitPrice")} htmlFor="purchase-line-price" required error={priceError}>
          <Input id="purchase-line-price" inputMode="decimal" className="tabular-nums" value={unitPrice} onChange={(e) => setUnitPrice(e.target.value)} aria-invalid={priceError != null} />
        </FormField>
        <FormField label={t("PlanningExecution:Trip")} htmlFor="purchase-line-trip" required error={tripError} floating="always">
          {/* DEVIATION(UI-LOOK-004): the trips the freight item's legs are on: nothing to create in place, a trip is never created in the Transport app (the Planboard creates trips, PO), approved in docs/designs/61-transport-app.md (Trips list and editor). */}
          <ReferenceCombobox id="purchase-line-trip" value={tripId} onChange={setTripId} options={trips.map((x) => ({ value: x.id, label: x.number }))} required aria-invalid={tripError != null} />
        </FormField>
      </form>
    </EntityDialog>
  );
}
