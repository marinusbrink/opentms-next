import { useAppFeatures } from "@/app/app-features";
import { RecordLink } from "@/components/ui/record-link";
import { useOrderIntake } from "@/domains/orders/order-intakes";
import { ORDER_SOURCES, enumKey, type TransportOrderDto } from "@/domains/orders/transport-orders";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { documentTracePath, recordPath } from "@/lib/use-open-record";

const CHIP = "inline-flex items-center rounded-full border border-brand-foreground/40 px-2 py-0.5 text-label font-medium text-brand-foreground whitespace-nowrap";

/* The order page's source chip (design 72 "UI design": "the source chip (Import, API) links to the intake and the
 * trace"): an order that came from an intake shows where it came from in the header. The chip links to the intake's
 * review page; next to it a second chip opens Integrations' trace of the source document (the intake names the
 * document). Each link only when the user may open it; orders typed in by hand show nothing. */
export function IntakeSourceChip({ order }: { order: Pick<TransportOrderDto, "source" | "sourceIntakeId"> | undefined }) {
  const { t } = useL();
  const features = useAppFeatures();
  const canOpen = useIsGranted("Orders.OrderIntakes") && features.isFeatureEnabled("Apps.Transport.Intake");
  const canTrace = useIsGranted("Integrations.InboundMessages") && features.isFeatureEnabled("Apps.Integrations.Received");
  const intake = useOrderIntake(canOpen && canTrace && order?.sourceIntakeId ? order.sourceIntakeId : undefined);
  if (!order?.sourceIntakeId) return null;
  const label = t(enumKey(ORDER_SOURCES, order.source) || "Orders:OrderSource.Import");
  const documentId = intake.data?.sourceSummary?.sourceDocumentId;
  return (
    <>
      {canOpen ? (
        <RecordLink to={recordPath("OrderIntake", order.sourceIntakeId)} className={`${CHIP} hover:bg-brand-hover`}>
          {label}
        </RecordLink>
      ) : (
        <span className={CHIP} data-slot="order-source">
          {label}
        </span>
      )}
      {documentId && (
        <RecordLink to={documentTracePath(documentId)} className={`${CHIP} hover:bg-brand-hover`}>
          {t("Orders:IntakeViewSource")}
        </RecordLink>
      )}
    </>
  );
}
