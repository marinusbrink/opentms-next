import { useState } from "react";

import { useAppFeatures } from "@/app/app-features";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { FeatureUnavailable } from "@/components/ui/feature-unavailable";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { RecordNotFound } from "@/components/ui/record-not-found";
import { Skeleton } from "@/components/ui/skeleton";
import { OrderEditor } from "@/apps/transport/orders/OrderEditor";
import { useTransportOrder, useTransportOrderTypeRows } from "@/domains/orders/transport-orders";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { toIsoDate } from "@/lib/format";
import { NEW_RECORD_ID, recordLoadState } from "@/lib/use-open-record";

export interface OrderPageProps {
  id: string;
  /** A new order of this type ("New per type"); the tenant's default type otherwise. */
  typeId?: string;
}

/* Design 61, "Order page, revised": the record page of a transport order (/transport/orders/{id}, Assumption 1), with
 * the page states of UI-PAGE-005 (skeleton, not found, error, permission denied, feature off) around the OrderEditor.
 * `/transport/orders/new` is the same page without a record (5: New opens the page directly): the default type (or
 * the one "New per type" names), today, the cursor in Customer. Both render the OrderEditor at the same place, so
 * after the first Save the editor stays as it is while the route moves to the new id. */
export function OrderPage({ id, typeId }: OrderPageProps) {
  const { t } = useL();
  const features = useAppFeatures();
  const isNew = id === NEW_RECORD_ID;
  const canView = useIsGranted("Orders.TransportOrders");
  const canUpdate = useIsGranted("Orders.TransportOrders.Update");
  const canCreate = useIsGranted("Orders.TransportOrders.Create");
  const order = useTransportOrder(canView && !isNew ? id : undefined);
  const types = useTransportOrderTypeRows(canView && canCreate && isNew);
  const [today] = useState(() => toIsoDate(new Date()));

  if (features.isLoaded && !features.isFeatureEnabled("Apps.Transport.Orders")) return <FeatureUnavailable />;
  if (!canView || (isNew && !canCreate)) return <PermissionDenied />;
  if (isNew) {
    if (types.isLoading) return <PageSkeleton />;
    const rows = types.data ?? [];
    const type = (typeId && rows.some((row) => row.id === typeId) ? typeId : undefined) ?? rows.find((row) => row.isDefault)?.id ?? (rows.length === 1 ? rows[0].id : undefined) ?? null;
    return <OrderEditor mode="order" readOnly={false} newOrder={{ transportOrderTypeId: type, orderDate: today }} />;
  }

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
