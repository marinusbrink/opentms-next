import type { fetchRelationFinancialDefaults } from "@/domains/financial/rate-lines";
import type { TransportOrderFinancialTermsInputDto } from "@/domains/orders/transport-orders";

/**
 * The customer's financial defaults as the order's terms (revenue side), as the server copies them (design 58, build
 * note 7; Financial's RelationFinancialSettings): the revenue lists in position order and, given the order date, only
 * the default discounts or surcharges valid on it. The order page copies them when the customer of a new order is
 * chosen, and asks before it does on a stored one (design 61 "Order page, revised" 5: the rule is unchanged).
 */
export function termsFromDefaults(defaults: Awaited<ReturnType<typeof fetchRelationFinancialDefaults>>, asOf?: string | null): TransportOrderFinancialTermsInputDto | undefined {
  if (!defaults) return undefined;
  const validOn = (d: { validFrom?: string; validTill?: string | null }) => !asOf || ((!d.validFrom || d.validFrom <= asOf) && (!d.validTill || asOf <= d.validTill));
  return {
    currencyCode: defaults.currencyCode ?? "EUR",
    vatPercentageId: defaults.vatPercentageId ?? null,
    paymentTermDays: defaults.paymentTermDays ?? null,
    priceListIds: [...(defaults.revenuePriceLists ?? [])].sort((a, b) => (a.position ?? 0) - (b.position ?? 0)).map((p) => p.priceListId!).filter(Boolean),
    multipleMatches: defaults.revenueMultipleMatches,
    discountsOrSurcharges: (defaults.discountsOrSurcharges ?? []).filter((d) => d.discountOrSurchargeId && validOn(d)).map((d) => ({ discountOrSurchargeId: d.discountOrSurchargeId!, source: 1 as const })),
    sendFreightDocumentsWithInvoice: defaults.sendFreightDocumentsWithInvoice ?? false,
  };
}
