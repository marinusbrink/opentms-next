import type { KnownPriceList } from "@/domains/financial/price-lists";
import { orderedRevenueBases } from "@/domains/financial/price-lists";
import type { fetchRelationFinancialDefaults } from "@/domains/financial/rate-lines";
import type { TransportOrderFinancialTermsInputDto } from "@/domains/orders/transport-orders";

type Defaults = Awaited<ReturnType<typeof fetchRelationFinancialDefaults>>;

const revenueListsOf = (defaults: NonNullable<Defaults>) => [...(defaults.revenuePriceLists ?? [])].sort((a, b) => (a.position ?? 0) - (b.position ?? 0));

/**
 * The customer's financial defaults as the order's terms (revenue side), as the server copies them (design 58, build
 * note 7; Financial's RelationFinancialSettings): the revenue lists in position order with the bases switched on
 * (design 61 "Order page review" 3: `revenueChargedBases`) and, given the order date, only the default discounts or
 * surcharges valid on it. The order page copies them when the customer of a new order is chosen, and asks before it does
 * on a stored one (design 61 "Order page, legacy tab structure" 6: the rule is unchanged).
 */
export function termsFromDefaults(defaults: Defaults, asOf?: string | null): TransportOrderFinancialTermsInputDto | undefined {
  if (!defaults) return undefined;
  const validOn = (d: { validFrom?: string; validTill?: string | null }) => !asOf || ((!d.validFrom || d.validFrom <= asOf) && (!d.validTill || asOf <= d.validTill));
  return {
    currencyCode: defaults.currencyCode ?? "EUR",
    vatPercentageId: defaults.vatPercentageId ?? null,
    paymentTermDays: defaults.paymentTermDays ?? null,
    priceListIds: revenueListsOf(defaults).map((p) => p.priceListId!).filter(Boolean),
    chargedBases: orderedRevenueBases(defaults.revenueChargedBases ?? []),
    multipleMatches: defaults.revenueMultipleMatches,
    discountsOrSurcharges: (defaults.discountsOrSurcharges ?? []).filter((d) => d.discountOrSurchargeId && validOn(d)).map((d) => ({ discountOrSurchargeId: d.discountOrSurchargeId!, source: 1 as const })),
    sendFreightDocumentsWithInvoice: defaults.sendFreightDocumentsWithInvoice ?? false,
  };
}

/** The basis and name of each copied revenue list, so the Financial tab shows it in its basis row at once. */
export function knownListsOfDefaults(defaults: Defaults): KnownPriceList[] {
  if (!defaults) return [];
  return revenueListsOf(defaults).flatMap((p) => (p.priceListId && p.basis != null ? [{ id: p.priceListId, basis: p.basis, label: [p.code, p.name].filter(Boolean).join(" ") }] : []));
}
