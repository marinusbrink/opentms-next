import { Spinner } from "@/components/ui/spinner";
import { TotalsBlock, type TotalsLine } from "@/apps/transport/components/TotalsBlock";
import type { PricePreviewState } from "@/apps/transport/orders/use-price-preview";
import { RATE_LINE_KIND, type RateLineSetDto } from "@/domains/financial/rate-lines";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* The order's revenue overview (design 61 "Order page, legacy tab structure" 6, on the Financial tab), read-only: the freight items (the sum of the items'
 * stored totals the server reports per item), the order-level lines (fixed price, additional services, discounts and
 * surcharges, other order-level lines), and the total the server holds, with the CalculatingIndicator while they are
 * recalculated (flow 2). The order-level lines themselves are managed on the Financial tab, the additional services on
 * General. While an unsaved change is
 * previewed the preview's figures show instead, dimmed while the next one is asked for (UI-FB-006). */

export interface OrderTotalsProps {
  rateLines: RateLineSetDto | undefined;
  /** The ids of the order's stored freight items. */
  lineIds: string[];
  currency: string;
  preview: PricePreviewState;
}

export function OrderTotals({ rateLines, lineIds, currency, preview }: OrderTotalsProps) {
  const { t } = useL();
  const ids = new Set(lineIds.map((id) => id.toLowerCase()));
  const totals = rateLines?.totals;
  const lines = rateLines?.lines ?? [];
  const orderLevel = lines.filter((line) => !line.orderLineId);
  const sum = (items: { amount?: number }[]) => items.reduce((total, item) => total + (item.amount ?? 0), 0);
  const byAnchor = (totals?.byAnchor ?? []).filter((a) => a.anchorId && ids.has(a.anchorId.toLowerCase()));
  const items = byAnchor.length > 0 ? sum(byAnchor) : sum(lines.filter((line) => line.orderLineId && ids.has(line.orderLineId.toLowerCase())));
  const otherOrderLevel = sum(orderLevel.filter((line) => line.kind !== RATE_LINE_KIND.FixedPrice && line.kind !== RATE_LINE_KIND.AdditionalService && line.kind !== RATE_LINE_KIND.DiscountOrSurcharge));

  const shown: TotalsLine[] = preview.preview
    ? [
        { label: t("Orders:Lines"), value: preview.preview.linesAmount },
        { label: t("Financial:DiscountsAndSurcharges"), value: preview.preview.discountsAndSurchargesAmount },
        { label: t("Financial:Total"), value: preview.preview.totalAmount, grand: true },
      ]
    : [
        { label: t("Orders:Lines"), value: items },
        ...(totals?.hasFixedPrice ? [{ label: t("Financial:FixedPrice"), value: sum(orderLevel.filter((line) => line.kind === RATE_LINE_KIND.FixedPrice)) }] : []),
        ...(otherOrderLevel !== 0 ? [{ label: t("Orders:OrderLevelLines"), value: otherOrderLevel }] : []),
        { label: t("Financial:AdditionalServices"), value: totals?.additionalServicesAmount },
        { label: t("Financial:DiscountsAndSurcharges"), value: totals?.discountsAndSurchargesAmount },
        { label: t("Financial:Total"), value: totals?.total, grand: true },
      ];

  return (
    <div className={cn("relative", preview.previewing && "opacity-50")} data-slot="order-totals">
      {preview.previewing && (
        <div className="absolute inset-0 z-10 flex items-center justify-center">
          <Spinner />
        </div>
      )}
      <TotalsBlock
        lines={shown}
        currencyCode={preview.preview?.currencyCode ?? rateLines?.currencyCode ?? currency}
        state={preview.preview ? undefined : rateLines?.state}
        footer={preview.preview ? <span className="text-label text-muted-foreground">{t("Orders:PreviewPrices")}</span> : undefined}
      />
    </div>
  );
}
