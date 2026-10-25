import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback } from "react";

import type { GridRequest, GridResponse } from "@/components/ui/opentms-grid";
import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";

/* Consignment notes (CMR; design 58 §CMR, design 61 "Documents"): the printed documents of an order, a trip or the
 * tenant, the print command (one document per order among the selected lines, so a print returns a list; design 58
 * build note 11) and the authenticated download. The profiles' lookup and detail are read here too, for the Print
 * wizard's profile step; the profiles' own list lives in the Master data app. */

type Schemas = components["schemas"];

export type ConsignmentNoteDocumentDto = Schemas["OpenTms.Orders.ConsignmentNotes.ConsignmentNoteDocumentDto"];
export type PrintConsignmentNoteDto = Schemas["OpenTms.Orders.ConsignmentNotes.PrintConsignmentNoteDto"];
export type ConsignmentNoteOverridesDto = Schemas["OpenTms.Orders.ConsignmentNotes.ConsignmentNoteOverridesDto"];
export type ConsignmentNoteProfileDto = Schemas["OpenTms.Orders.ConsignmentNotes.ConsignmentNoteProfileDto"];
export type CmrPartySource = Schemas["OpenTms.Orders.ConsignmentNotes.CmrPartySource"];
export type CmrGoodsColumn = Schemas["OpenTms.Orders.ConsignmentNotes.CmrGoodsColumn"];
export type CmrInstructionItem = Schemas["OpenTms.Orders.ConsignmentNotes.CmrInstructionItem"];
export type CmrRemarkItem = Schemas["OpenTms.Orders.ConsignmentNotes.CmrRemarkItem"];
export type ProfileLookupItem = Schemas["OpenTms.Platform.ReferenceData.ReferenceDataLookupDto"];

export const CMR_PARTY_SOURCES: { value: CmrPartySource; key: string }[] = [
  { value: 1, key: "Orders:CmrPartySource.Customer" },
  { value: 2, key: "Orders:CmrPartySource.OriginAddress" },
  { value: 3, key: "Orders:CmrPartySource.DestinationAddress" },
  { value: 4, key: "Orders:CmrPartySource.OwnCompany" },
  { value: 5, key: "Orders:CmrPartySource.Carrier" },
  { value: 6, key: "Orders:CmrPartySource.Shipper" },
  { value: 7, key: "Orders:CmrPartySource.TripDeparture" },
  { value: 8, key: "Orders:CmrPartySource.TripArrival" },
  { value: 9, key: "Orders:CmrPartySource.Empty" },
];

/** The party boxes of a profile that a print may override (design 58). */
export const CMR_PARTY_BOXES = [
  { field: "box1Sender", key: "Orders:Box1Sender" },
  { field: "box2Consignee", key: "Orders:Box2Consignee" },
  { field: "box3DeliveryPlace", key: "Orders:Box3DeliveryPlace" },
  { field: "box4TakingOverPlace", key: "Orders:Box4TakingOverPlace" },
  { field: "box16Carrier", key: "Orders:Box16Carrier" },
  { field: "box17SuccessiveCarrier", key: "Orders:Box17SuccessiveCarrier" },
  { field: "box21EstablishedBy", key: "Orders:Box21EstablishedBy" },
] as const;

export type CmrPartyBoxField = (typeof CMR_PARTY_BOXES)[number]["field"];

export const consignmentNoteKeys = {
  all: ["orders", "consignmentNotes"] as const,
  list: () => ["orders", "consignmentNotes", "list"] as const,
  profileLookup: () => ["orders", "consignmentNoteProfiles", "lookup"] as const,
  profile: (id: string | undefined) => ["orders", "consignmentNoteProfiles", "detail", id?.toLowerCase()] as const,
};

export interface ConsignmentNoteListFilters {
  orderId?: string;
  tripId?: string;
  printedFrom?: string;
  printedTo?: string;
}

export async function fetchConsignmentNotes(request: GridRequest, filters: ConsignmentNoteListFilters): Promise<GridResponse<ConsignmentNoteDocumentDto>> {
  const sort = request.sortModels[0];
  const { data, error } = await apiClient.GET("/api/orders/consignment-note/document-list", {
    params: {
      query: {
        StartRow: request.startRow,
        EndRow: request.endRow,
        WildcardSearch: request.wildcardSearch ?? undefined,
        SortBy: sort?.colId,
        SortDir: sort?.sort,
        OrderId: filters.orderId || undefined,
        TripId: filters.tripId || undefined,
        PrintedFrom: filters.printedFrom || undefined,
        PrintedTo: filters.printedTo || undefined,
      },
    },
  });
  if (error) throw error;
  return { rows: data?.rows ?? [], totalCount: Number(data?.totalCount ?? 0), filteredCount: Number(data?.filteredCount ?? 0) };
}

export function useConsignmentNotesGridFetcher(filters: ConsignmentNoteListFilters) {
  const key = JSON.stringify(filters);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  return useCallback((request: GridRequest) => fetchConsignmentNotes(request, JSON.parse(key) as ConsignmentNoteListFilters), [key]);
}

/** The documents of one record (an order's or a trip's Documents tab): the whole list, bounded by the page size. */
export function useConsignmentNotesOf(filters: ConsignmentNoteListFilters, enabled = true) {
  return useQuery({
    queryKey: [...consignmentNoteKeys.list(), filters],
    enabled,
    queryFn: () =>
      fetchConsignmentNotes({ startRow: 0, endRow: 200, sortModels: [{ colId: "printedAt", sort: "desc" }], columnFilters: {}, wildcardSearch: null, rowGroupCols: [], groupKeys: [] }, filters),
  });
}

export function useConsignmentNoteProfileLookup(enabled = true) {
  return useQuery({
    queryKey: consignmentNoteKeys.profileLookup(),
    enabled,
    staleTime: 60 * 1000,
    queryFn: async (): Promise<ProfileLookupItem[]> => {
      const { data, error } = await apiClient.GET("/api/orders/consignment-note-profile/lookup", { params: { query: { take: 500 } } });
      if (error) throw error;
      return data ?? [];
    },
  });
}

/** A profile's settings: the defaults the Print wizard shows and lets the user override for this print. */
export function useConsignmentNoteProfile(id: string | undefined) {
  return useQuery({
    queryKey: consignmentNoteKeys.profile(id),
    enabled: id != null,
    queryFn: async (): Promise<ConsignmentNoteProfileDto> => {
      const { data, error } = await apiClient.GET("/api/orders/consignment-note-profile/{id}", { params: { path: { id: id! } } });
      if (error) throw error;
      return data!;
    },
  });
}

/** Prints: one document per order among the selected lines, so the result is a list (design 58, build note 11). */
export async function printConsignmentNote(request: PrintConsignmentNoteDto): Promise<ConsignmentNoteDocumentDto[]> {
  const { data, error } = await apiClient.POST("/api/orders/consignment-note/print", { body: request });
  if (error) throw error;
  return data ?? [];
}

export function usePrintConsignmentNote() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: printConsignmentNote,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: consignmentNoteKeys.all }),
  });
}

/** Fetches the stored PDF through the authenticated client; the caller shows or saves the blob. */
export async function downloadConsignmentNote(id: string): Promise<{ blob: Blob; fileName: string }> {
  const { data, error, response } = await apiClient.GET("/api/orders/consignment-note/{id}/download", { params: { path: { id } }, parseAs: "blob" });
  if (error) throw error;
  const disposition = response.headers.get("content-disposition") ?? "";
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)/i.exec(disposition);
  return { blob: data as Blob, fileName: match ? decodeURIComponent(match[1]) : `${id}.pdf` };
}

/** Hands a blob to the browser: `open` shows it in a new tab (Print), `save` downloads it (Download). */
export function deliverBlob(blob: Blob, fileName: string, mode: "open" | "save") {
  const url = URL.createObjectURL(blob);
  if (mode === "open") {
    window.open(url, "_blank", "noopener");
  } else {
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
  }
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
