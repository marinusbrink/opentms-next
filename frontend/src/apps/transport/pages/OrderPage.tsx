import { useAppFeatures } from "@/app/app-features";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { FeatureUnavailable } from "@/components/ui/feature-unavailable";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { RecordNotFound } from "@/components/ui/record-not-found";
import { Skeleton } from "@/components/ui/skeleton";
import { OrderEditor } from "@/apps/transport/orders/OrderEditor";
import { useTransportOrder } from "@/domains/orders/transport-orders";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { recordLoadState } from "@/lib/use-open-record";

export interface OrderPageProps {
  id: string;
}

/* Design 61, "Order editor": the record page of a transport order (/transport/orders/{id}, Assumption 1), with the
 * page states of UI-PAGE-005 (skeleton, not found, error, permission denied, feature off) around the OrderEditor. */
export function OrderPage({ id }: OrderPageProps) {
  const { t } = useL();
  const features = useAppFeatures();
  const canView = useIsGranted("Orders.TransportOrders");
  const canUpdate = useIsGranted("Orders.TransportOrders.Update");
  const order = useTransportOrder(canView ? id : undefined);

  if (features.isLoaded && !features.isFeatureEnabled("Apps.Transport.Orders")) return <FeatureUnavailable />;
  if (!canView) return <PermissionDenied />;

  const state = recordLoadState(order);
  if (state === "loading") return <PageSkeleton />;
  if (state === "not-found") return <RecordNotFound />;
  if (state === "error")
    return (
      <Alert variant="destructive" className="m-6">
        <AlertDescription>{getApiErrorMessage(order.error, t)}</AlertDescription>
      </Alert>
    );
  return <OrderEditor mode="order" order={order.data} readOnly={!canUpdate} />;
}

export function PageSkeleton() {
  return (
    <div className="flex h-full flex-col gap-4 p-6" data-slot="record-page-skeleton">
      <Skeleton className="h-10 w-full" />
      <Skeleton className="h-16 w-2/3" />
      <Skeleton className="min-h-0 flex-1 w-full" />
    </div>
  );
}
