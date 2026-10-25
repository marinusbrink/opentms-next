import { useMutation } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import type { Formatter } from "@/lib/format";
import type { components } from "@/lib/api/generated/schema";
import { createReferenceDataHooks } from "@/lib/reference-data/crud-hooks";

/* The Financial reference data shown in the Master data app (design 52): Invoicing (VAT percentages, outbound and
 * inbound invoice types) and Pricing (additional services, discounts and surcharges). Row = grid, Dto = editor,
 * Create/Update = what the editor sends; the schema names carry the backend namespaces. */

type Schemas = components["schemas"];

// ── enums (value → localization key) ──────────────────────────────────────────

export type VatKind = Schemas["OpenTms.Financial.Invoicing.VatKind"];
export const VAT_KINDS: { value: VatKind; key: string }[] = [
  { value: 1, key: "Financial:VatKind.Standard" },
  { value: 2, key: "Financial:VatKind.Reduced" },
  { value: 3, key: "Financial:VatKind.Zero" },
  { value: 4, key: "Financial:VatKind.Exempt" },
  { value: 5, key: "Financial:VatKind.ReverseCharge" },
  { value: 6, key: "Financial:VatKind.IntraCommunity" },
  { value: 7, key: "Financial:VatKind.Export" },
];

export type OutboundInvoiceKind = Schemas["OpenTms.Financial.Invoicing.OutboundInvoiceKind"];
export const OUTBOUND_INVOICE_KINDS: { value: OutboundInvoiceKind; key: string }[] = [
  { value: 1, key: "Financial:OutboundInvoiceKind.Invoice" },
  { value: 2, key: "Financial:OutboundInvoiceKind.CreditNote" },
];

export type InboundInvoiceKind = Schemas["OpenTms.Financial.Invoicing.InboundInvoiceKind"];
export const INBOUND_INVOICE_KINDS: { value: InboundInvoiceKind; key: string }[] = [
  { value: 1, key: "Financial:InboundInvoiceKind.CarrierInvoice" },
  { value: 2, key: "Financial:InboundInvoiceKind.Other" },
];

export type ServiceUnit = Schemas["OpenTms.Financial.Tariffs.ServiceUnit"];
export const SERVICE_UNITS: { value: ServiceUnit; key: string }[] = [
  { value: 1, key: "Financial:ServiceUnit.Piece" },
  { value: 2, key: "Financial:ServiceUnit.Hour" },
  { value: 3, key: "Financial:ServiceUnit.Kilometre" },
  { value: 4, key: "Financial:ServiceUnit.Kilogram" },
  { value: 5, key: "Financial:ServiceUnit.Stop" },
  { value: 6, key: "Financial:ServiceUnit.Day" },
];

export type DiscountOrSurchargeDirection = Schemas["OpenTms.Financial.Tariffs.DiscountOrSurchargeDirection"];
export const DIRECTIONS: { value: DiscountOrSurchargeDirection; key: string }[] = [
  { value: 1, key: "Financial:Direction.Surcharge" },
  { value: 2, key: "Financial:Direction.Discount" },
];

export type DiscountOrSurchargeValueKind = Schemas["OpenTms.Financial.Tariffs.DiscountOrSurchargeValueKind"];
export const VALUE_KINDS: { value: DiscountOrSurchargeValueKind; key: string }[] = [
  { value: 1, key: "Financial:ValueKind.FixedAmount" },
  { value: 2, key: "Financial:ValueKind.Percentage" },
  { value: 3, key: "Financial:ValueKind.PerKilometre" },
];

/** Fixed amounts and amounts per kilometre carry a currency; a percentage does not (design 52, assessment §5). */
export const needsCurrency = (kind: DiscountOrSurchargeValueKind | null | undefined) => kind !== 2;

export type DiscountOrSurchargeSide = Schemas["OpenTms.Financial.Tariffs.DiscountOrSurchargeSide"];
export const SIDES: { value: DiscountOrSurchargeSide; key: string }[] = [
  { value: 1, key: "Financial:Side.Revenue" },
  { value: 2, key: "Financial:Side.Cost" },
  { value: 3, key: "Financial:Side.Both" },
];

export type RegionAppliesAt = Schemas["OpenTms.Financial.Tariffs.RegionAppliesAt"];
export const REGION_APPLIES_AT: { value: RegionAppliesAt; key: string }[] = [
  { value: 1, key: "Financial:AppliesAt.Loading" },
  { value: 2, key: "Financial:AppliesAt.Unloading" },
  { value: 3, key: "Financial:AppliesAt.Either" },
];

/** The `[Flags]` DaysOfWeek (Monday = 1 … Sunday = 64); the schema lists only the single flags, so combinations are cast. */
export type DaysOfWeek = Schemas["OpenTms.Financial.Tariffs.DaysOfWeek"];
export const WEEK_DAYS: { flag: number; key: string }[] = [
  { flag: 1, key: "Financial:Day.Monday" },
  { flag: 2, key: "Financial:Day.Tuesday" },
  { flag: 4, key: "Financial:Day.Wednesday" },
  { flag: 8, key: "Financial:Day.Thursday" },
  { flag: 16, key: "Financial:Day.Friday" },
  { flag: 32, key: "Financial:Day.Saturday" },
  { flag: 64, key: "Financial:Day.Sunday" },
];
export const ALL_DAYS = 127;
export const asDaysOfWeek = (flags: number): DaysOfWeek => flags as DaysOfWeek;

export const keyOf = (items: { value: number; key: string }[], value: number | null | undefined) => items.find((i) => i.value === value)?.key ?? "";

// ── VAT percentages ───────────────────────────────────────────────────────────

export type VatPercentageRow = Schemas["OpenTms.Financial.Invoicing.VatPercentageRowDto"];
export type VatPercentageDto = Schemas["OpenTms.Financial.Invoicing.VatPercentageDto"];
export type VatPercentageCreateDto = Schemas["OpenTms.Financial.Invoicing.VatPercentageCreateDto"];
export type VatPercentageUpdateDto = Schemas["OpenTms.Financial.Invoicing.VatPercentageUpdateDto"];

export const vatPercentages = createReferenceDataHooks<
  VatPercentageRow,
  VatPercentageDto,
  VatPercentageCreateDto,
  VatPercentageUpdateDto,
  { includeArchived?: boolean }
>("vatPercentages", "/api/financial/vat-percentage");

/** Moves the tenant's one default VAT category (design 52). */
export function useSetDefaultVatPercentage() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST("/api/financial/vat-percentage/{id}/set-default", { params: { path: { id } } });
      if (error) throw error;
    },
  });
}

// ── Outbound and inbound invoice types ────────────────────────────────────────

export type OutboundInvoiceTypeRow = Schemas["OpenTms.Financial.Invoicing.OutboundInvoiceTypeRowDto"];
export type OutboundInvoiceTypeDto = Schemas["OpenTms.Financial.Invoicing.OutboundInvoiceTypeDto"];
export type OutboundInvoiceTypeCreateDto = Schemas["OpenTms.Financial.Invoicing.OutboundInvoiceTypeCreateDto"];
export type OutboundInvoiceTypeUpdateDto = Schemas["OpenTms.Financial.Invoicing.OutboundInvoiceTypeUpdateDto"];

export const outboundInvoiceTypes = createReferenceDataHooks<
  OutboundInvoiceTypeRow,
  OutboundInvoiceTypeDto,
  OutboundInvoiceTypeCreateDto,
  OutboundInvoiceTypeUpdateDto,
  { includeArchived?: boolean }
>("outboundInvoiceTypes", "/api/financial/outbound-invoice-type");

/** Makes the type the default of its kind (one per kind, Assumption 5). */
export function useSetDefaultOutboundInvoiceType() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST("/api/financial/outbound-invoice-type/{id}/set-default", { params: { path: { id } } });
      if (error) throw error;
    },
  });
}

export type InboundInvoiceTypeRow = Schemas["OpenTms.Financial.Invoicing.InboundInvoiceTypeRowDto"];
export type InboundInvoiceTypeDto = Schemas["OpenTms.Financial.Invoicing.InboundInvoiceTypeDto"];
export type InboundInvoiceTypeCreateDto = Schemas["OpenTms.Financial.Invoicing.InboundInvoiceTypeCreateDto"];
export type InboundInvoiceTypeUpdateDto = Schemas["OpenTms.Financial.Invoicing.InboundInvoiceTypeUpdateDto"];

export const inboundInvoiceTypes = createReferenceDataHooks<
  InboundInvoiceTypeRow,
  InboundInvoiceTypeDto,
  InboundInvoiceTypeCreateDto,
  InboundInvoiceTypeUpdateDto,
  { includeArchived?: boolean }
>("inboundInvoiceTypes", "/api/financial/inbound-invoice-type");

// ── Additional services, discounts and surcharges ─────────────────────────────

export type AdditionalServiceRow = Schemas["OpenTms.Financial.Tariffs.AdditionalServiceRowDto"];
export type AdditionalServiceDto = Schemas["OpenTms.Financial.Tariffs.AdditionalServiceDto"];
export type AdditionalServiceCreateDto = Schemas["OpenTms.Financial.Tariffs.AdditionalServiceCreateDto"];
export type AdditionalServiceUpdateDto = Schemas["OpenTms.Financial.Tariffs.AdditionalServiceUpdateDto"];

export const additionalServices = createReferenceDataHooks<
  AdditionalServiceRow,
  AdditionalServiceDto,
  AdditionalServiceCreateDto,
  AdditionalServiceUpdateDto,
  { includeArchived?: boolean }
>("additionalServices", "/api/financial/additional-service");

export type DiscountOrSurchargeRow = Schemas["OpenTms.Financial.Tariffs.DiscountOrSurchargeRowDto"];
export type DiscountOrSurchargeDto = Schemas["OpenTms.Financial.Tariffs.DiscountOrSurchargeDto"];
export type DiscountOrSurchargeCreateDto = Schemas["OpenTms.Financial.Tariffs.DiscountOrSurchargeCreateDto"];
export type DiscountOrSurchargeUpdateDto = Schemas["OpenTms.Financial.Tariffs.DiscountOrSurchargeUpdateDto"];

export const discountsOrSurcharges = createReferenceDataHooks<
  DiscountOrSurchargeRow,
  DiscountOrSurchargeDto,
  DiscountOrSurchargeCreateDto,
  DiscountOrSurchargeUpdateDto,
  { includeArchived?: boolean }
>("discountsOrSurcharges", "/api/financial/discount-or-surcharge");

/** The list's current value, formatted by kind: `12.5%`, `25.00 EUR`, `0.1275 EUR/km` (design 52; UI-FMT-007: the code, never a symbol). */
export function formatDiscountOrSurchargeValue(
  f: Formatter,
  t: (key: string) => string,
  kind: DiscountOrSurchargeValueKind | null | undefined,
  value: number | null | undefined,
  currencyCode: string | null | undefined,
): string {
  if (value == null) return "";
  if (kind === 2) return f.percent(value);
  const amount = kind === 3 ? f.unitPrice(value) : f.amount(value);
  const withCurrency = currencyCode ? `${amount} ${currencyCode}` : amount;
  return kind === 3 ? `${withCurrency}${t("Financial:PerKilometreSuffix")}` : withCurrency;
}
