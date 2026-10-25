import { ViewPlaceholder } from "@/apps/AppPlaceholder";

/* Design 61, "Templates": the templates list (name, type, customer, recurrence, next date, active). The screen arrives
 * with the Orders backend (design 58); until then the page skeleton with its gates. */
export function TemplatesView() {
  return <ViewPlaceholder permission="Orders.TransportOrderTemplates" feature="Apps.Transport.Orders.Templates" />;
}
