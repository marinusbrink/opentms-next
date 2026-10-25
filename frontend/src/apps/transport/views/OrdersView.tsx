import { ViewPlaceholder } from "@/apps/AppPlaceholder";

export interface OrdersViewProps {
  /** The saved filter from the URL (today, tomorrow, this-week, open, drafts, all; UI-PAGE-004). */
  filter?: string;
}

/* Design 61, "Orders list": the server-side OpenTmsGrid with its command bar and saved filters. The screen arrives with
 * the Orders backend (design 58); until then the page skeleton with its gates. */
export function OrdersView({ filter }: OrdersViewProps) {
  return <ViewPlaceholder permission="Orders.TransportOrders" feature="Apps.Transport.Orders" filter={filter} />;
}
