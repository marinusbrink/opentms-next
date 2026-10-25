import { ViewPlaceholder } from "@/apps/AppPlaceholder";

/* Design 61 (design 57 §UI): the Financial domain group of the Master data app, Pricing → Relation financial settings.
 * The list and its editor arrive with the Financial backend (design 57); until then the page skeleton with its gates. */
export function RelationFinancialSettingsView() {
  return <ViewPlaceholder permission="Financial.RelationFinancialSettings" />;
}
