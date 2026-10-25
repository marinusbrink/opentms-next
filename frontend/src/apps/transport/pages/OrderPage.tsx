import { ViewPlaceholder } from "@/apps/AppPlaceholder";
import { useL } from "@/lib/i18n/LocalizationProvider";

export interface OrderPageProps {
  id: string;
}

/* Design 61, "Order editor": the record page (header with number and StatusBadge, ribbon per status, the tabs General,
 * Lines, Pricing, Planning, Documents, Record identification), reached through useOpenRecord. The screen arrives with
 * the Orders backend (design 58); until then the page skeleton with its gates. */
export function OrderPage({ id }: OrderPageProps) {
  const { t } = useL();
  return (
    <ViewPlaceholder permission="Orders.TransportOrders" feature="Apps.Transport.Orders" heading={t("App:Transport.Orders")}>
      <div data-slot="order-page" data-record-id={id} />
    </ViewPlaceholder>
  );
}
