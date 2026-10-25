import { ViewPlaceholder } from "@/apps/AppPlaceholder";

/* Design 61: the Orders domain group of the Master data app, Transport orders → Order attributes. The list and its
 * editor arrive with the Orders backend (design 58); until then the page skeleton with its gates. */
export function OrderAttributeDefinitionsView() {
  return <ViewPlaceholder permission="Orders.OrderAttributeDefinitions" />;
}
