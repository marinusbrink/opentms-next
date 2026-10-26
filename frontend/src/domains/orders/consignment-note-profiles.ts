import { useMutation } from "@tanstack/react-query";

import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";
import { createReferenceDataHooks } from "@/lib/reference-data/crud-hooks";

/* The consignment note (CMR) profiles of the Orders domain (design 58 "CMR"): which party fills each box, which goods
 * columns, instructions and remarks print, the fixed texts, the layout flags; one profile is the tenant's default.
 * Design-50 list contract plus set-default. */

type Schemas = components["schemas"];

export interface EnumOption<T extends number> {
  value: T;
  key: string;
}

const options = <T extends number>(prefix: string, names: readonly string[]): EnumOption<T>[] =>
  names.map((name, index) => ({ value: (index + 1) as T, key: `Orders:${prefix}.${name}` }));

export const keyOf = <T extends number>(items: EnumOption<T>[], value: T | null | undefined) => items.find((i) => i.value === value)?.key ?? "";

export type CmrPartySource = Schemas["OpenTms.Orders.ConsignmentNotes.CmrPartySource"];
export const CMR_PARTY_SOURCES = options<CmrPartySource>("CmrPartySource", [
  "Customer",
  "OriginAddress",
  "DestinationAddress",
  "OwnCompany",
  "Carrier",
  "Shipper",
  "TripDeparture",
  "TripArrival",
  "Empty",
]);

export type CmrGoodsColumn = Schemas["OpenTms.Orders.ConsignmentNotes.CmrGoodsColumn"];
export const CMR_GOODS_COLUMNS = options<CmrGoodsColumn>("CmrGoodsColumn", [
  "ContainerNumber",
  "PinCode",
  "SizeOrIso",
  "Quantity",
  "TransportUnit",
  "ProductCode",
  "Product",
  "Colli",
  "WeightKg",
  "Dimensions",
  "VolumeM3",
  "Barcode",
  "Positions",
  "Temperature",
  "InvoiceReference",
  "ColumnNames",
  "Totals",
]);

export type CmrInstructionItem = Schemas["OpenTms.Orders.ConsignmentNotes.CmrInstructionItem"];
export const CMR_INSTRUCTION_ITEMS = options<CmrInstructionItem>("CmrInstructionItem", ["ShipperInstructions", "FixedDescriptions", "SpecialAgreements", "LineRemarks"]);

export type CmrRemarkItem = Schemas["OpenTms.Orders.ConsignmentNotes.CmrRemarkItem"];
export const CMR_REMARK_ITEMS = options<CmrRemarkItem>("CmrRemarkItem", [
  "Vehicles",
  "OrderNumber",
  "CustomerReference",
  "TripNumbers",
  "LineRemarks",
  "StopReferences",
  "DriverInstructions",
  "InvoiceReference",
  "Barcode",
  "Temperature",
  "SpecialAgreements",
  "ShippingCompany",
  "ShipperInstructions",
  "OriginReference",
  "DestinationReference",
]);

/** The party boxes of the profile, in CMR order, with their field and label key. */
export const CMR_PARTY_BOXES = [
  { field: "box1Sender", labelKey: "Orders:Box1Sender" },
  { field: "box2Consignee", labelKey: "Orders:Box2Consignee" },
  { field: "box3DeliveryPlace", labelKey: "Orders:Box3DeliveryPlace" },
  { field: "box4TakingOverPlace", labelKey: "Orders:Box4TakingOverPlace" },
  { field: "box16Carrier", labelKey: "Orders:Box16Carrier" },
  { field: "box17SuccessiveCarrier", labelKey: "Orders:Box17SuccessiveCarrier" },
  { field: "box21EstablishedBy", labelKey: "Orders:Box21EstablishedBy" },
] as const;
export type CmrPartyBoxField = (typeof CMR_PARTY_BOXES)[number]["field"];

/** ConsignmentNoteConsts: 1 to 5 copies, 3 by default. */
export const MIN_COPIES = 1;
export const MAX_COPIES = 5;
export const DEFAULT_COPIES = 3;

export type ConsignmentNoteProfileRow = Schemas["OpenTms.Orders.ConsignmentNotes.ConsignmentNoteProfileRowDto"];
export type ConsignmentNoteProfileDto = Schemas["OpenTms.Orders.ConsignmentNotes.ConsignmentNoteProfileDto"];
export type ConsignmentNoteProfileCreateDto = Schemas["OpenTms.Orders.ConsignmentNotes.ConsignmentNoteProfileCreateDto"];
export type ConsignmentNoteProfileUpdateDto = Schemas["OpenTms.Orders.ConsignmentNotes.ConsignmentNoteProfileUpdateDto"];

export const consignmentNoteProfiles = createReferenceDataHooks<
  ConsignmentNoteProfileRow,
  ConsignmentNoteProfileDto,
  ConsignmentNoteProfileCreateDto,
  ConsignmentNoteProfileUpdateDto,
  { includeArchived?: boolean }
>("consignmentNoteProfiles", "/api/orders/consignment-note-profile");

/** Makes the profile the tenant's one default (the previous default loses the flag). */
export function useSetDefaultConsignmentNoteProfile() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST("/api/orders/consignment-note-profile/{id}/set-default", { params: { path: { id } } });
      if (error) throw error;
    },
  });
}
