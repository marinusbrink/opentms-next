import { useMemo, useState } from "react";
import { Plus } from "lucide-react";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { RateLineDialog, RateLinesGrid } from "@/apps/transport/orders/RateLines";
import { RATE_LINE_KIND, type RateLineSetDto } from "@/domains/financial/rate-lines";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The Additional services tab of General (design 61 "Order page, legacy tab structure" 5): the order-level rate
 * lines of kind additional service, with Add additional service (picks from the additional-services list, which
 * fills the ledger account, cost centre and unit price; RateLineDialog) and Remove (the trash icon). These are stored
 * lines, each a command of its own (design 57), so they exist once the order is stored; before that the tab says so. */

export interface AdditionalServicesProps {
  /** The stored order; undefined for a new order or a template. */
  orderId: string | undefined;
  rateLines: RateLineSetDto | undefined;
  currency: string;
  editable: boolean;
}

export function AdditionalServices({ orderId, rateLines, currency, editable }: AdditionalServicesProps) {
  const { t } = useL();
  const [adding, setAdding] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const rows = useMemo(() => (rateLines?.lines ?? []).filter((line) => !line.orderLineId && line.kind === RATE_LINE_KIND.AdditionalService), [rateLines]);

  if (!orderId) {
    return (
      <p className="text-sm text-muted-foreground" data-slot="services-after-save">
        {t("Orders:AdditionalServicesAfterSave")}
      </p>
    );
  }
  return (
    <div className="flex flex-col gap-2" data-slot="additional-services">
      {error && (
        <Alert variant="destructive">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}
      <RateLinesGrid
        rows={rows}
        currency={rateLines?.currencyCode ?? currency}
        editable={editable}
        onError={setError}
        heightClass="h-64"
        toolbar={
          <Button type="button" size="sm" variant="outline" onClick={() => setAdding(true)}>
            <Plus /> {t("Financial:AddAdditionalService")}
          </Button>
        }
      />
      {adding && <RateLineDialog kind="service" orderId={orderId} orderLineId={null} currency={rateLines?.currencyCode ?? currency} onClose={() => setAdding(false)} />}
    </div>
  );
}
