import { TriangleAlert } from "lucide-react";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { tripRequestWarningText, type TransportOrderTripRequestDto } from "@/domains/orders/transport-orders";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The trip request's warning (design 58 build note 19, design 59 build note 14): Planning could not create the order's
 * "Tevens trip" (a type, relation, vehicle or driver it does not know) and the legs stay unplanned. Three places, each
 * with tone tokens only (UI-TOK-005): the Tevens-trip section shows the full text with its time (`Alert` warning,
 * UI-FB-008), the page header a badge that leads to the Planning tab, and the Orders list an icon with a tooltip. */

/** The warning in the Tevens-trip section of the Planning tab. */
export function TripRequestWarningAlert({ request }: { request: TransportOrderTripRequestDto | null | undefined }) {
  const { t } = useL();
  const f = useFormat();
  const text = tripRequestWarningText(t, request);
  if (!text) return null;
  return (
    <Alert variant="warning" className="mb-3" data-slot="trip-request-warning">
      <TriangleAlert aria-hidden="true" />
      <AlertDescription>
        <p>{text}</p>
        {request?.warningAt && <p className="text-label">{t("Orders:WarningSince").replace("{0}", f.dateTime(request.warningAt))}</p>}
      </AlertDescription>
    </Alert>
  );
}

/** The page-level badge in the order page's header; it opens the Planning tab, where the full text stands. */
export function TripRequestWarningBadge({ request, onOpen }: { request: TransportOrderTripRequestDto | null | undefined; onOpen: () => void }) {
  const { t } = useL();
  const text = tripRequestWarningText(t, request);
  if (!text) return null;
  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <button
            type="button"
            data-slot="trip-request-warning-badge"
            onClick={onOpen}
            className="inline-flex h-5 items-center gap-1 rounded-4xl border border-tone-warning/30 bg-tone-warning-bg px-2 text-xs font-medium text-tone-warning focus-visible:ring-[3px] focus-visible:ring-ring/50 focus-visible:outline-none"
          />
        }
      >
        <TriangleAlert className="size-3" aria-hidden="true" />
        {t("Orders:TripNotCreated")}
      </TooltipTrigger>
      <TooltipContent className="max-w-sm">{text}</TooltipContent>
    </Tooltip>
  );
}

/** The Orders list's indicator for a row with a trip request warning; the row carries the code only (not the field),
 * so the tooltip says what happened and where the details are. */
export function TripRequestWarningCell({ value }: { value: string | null | undefined }) {
  const { t } = useL();
  if (!value) return null;
  const label = t("Orders:TripNotCreated");
  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <span role="img" aria-label={label} data-slot="trip-request-warning-cell" className="inline-flex items-center text-tone-warning">
            <TriangleAlert className="size-4" aria-hidden="true" />
          </span>
        }
      />
      <TooltipContent className="max-w-sm">{t("Orders:TripNotCreatedHint")}</TooltipContent>
    </Tooltip>
  );
}
