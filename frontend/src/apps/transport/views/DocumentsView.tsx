import { ViewPlaceholder } from "@/apps/AppPlaceholder";

/* Design 61, "Documents view": the printed CMRs of the tenant (number, printed at, order, trip, lines, profile, by;
 * download). The CMR download needs Orders.TransportOrders (design 56, security quickscan). The screen arrives with the
 * Orders backend (design 58); until then the page skeleton with its gates. */
export function DocumentsView() {
  return <ViewPlaceholder permission="Orders.TransportOrders" feature="Apps.Transport.Documents" />;
}
