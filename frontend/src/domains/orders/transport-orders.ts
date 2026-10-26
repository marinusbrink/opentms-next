import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback } from "react";

import type { GridExportChoice } from "@/components/ui/grid-export-dialog";
import type { GridRequest, GridResponse, GridSelectionDto } from "@/components/ui/opentms-grid";
import { apiClient } from "@/lib/api/client";
import { withStatus } from "@/lib/api/errors";
import type { components } from "@/lib/api/generated/schema";
import { fallbackFileName, fileDateStamp, fileNameFromContentDisposition, saveBlob } from "@/lib/browser";
import { useRecordUpdates } from "@/lib/realtime/use-record-updates";

/* The transport orders of the Transport app (designs 58 and 61): the list, the record, the section commands of the
 * order aggregate (header, lines, stop pattern, references, financial terms, trip request, attributes), the status
 * commands (confirm, cancel, approve, revise, archive, delete) and the price preview. Every call goes through the
 * generated client; the hooks narrow the DTO types to the generated schema. The record's derived values (revenue,
 * cost, margin, progress, CO₂) arrive by push: `useTransportOrderUpdates` watches `Orders.TransportOrder` (flow 2). */

type Schemas = components["schemas"];

export type TransportOrderRow = Schemas["OpenTms.Orders.TransportOrders.TransportOrderRowDto"];
export type TransportOrderDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderDto"];
export type TransportOrderCreateDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderCreateDto"];
export type TransportOrderUpdateDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderUpdateDto"];
export type TransportOrderLineDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderLineDto"];
export type TransportOrderLineInputDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderLineInputDto"];
export type TransportOrderStopDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderStopDto"];
export type TransportOrderStopInputDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderStopInputDto"];
export type TransportOrderDefaultStopDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderDefaultStopDto"];
export type TransportOrderDefaultStopInputDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderDefaultStopInputDto"];
export type TransportOrderReferenceDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderReferenceEntryDto"];
export type TransportOrderMeasurementDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderMeasurementDto"];
export type TransportOrderFinancialTermsDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderFinancialTermsDto"];
export type TransportOrderFinancialTermsInputDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderFinancialTermsInputDto"];
export type TransportOrderTripRequestDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderTripRequestDto"];
export type TransportOrderTripRequestInputDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderTripRequestInputDto"];
export type TransportOrderSummaryDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderSummaryDto"];
export type TransportOrderStatus = Schemas["OpenTms.Orders.TransportOrders.TransportOrderStatus"];
export type TransportOrderView = Schemas["OpenTms.Orders.TransportOrders.TransportOrderView"];
export type ExecutionProgress = Schemas["OpenTms.Orders.TransportOrders.ExecutionProgress"];
export type PaymentInstruction = Schemas["OpenTms.Orders.TransportOrders.PaymentInstruction"];
export type OrderSource = Schemas["OpenTms.Orders.TransportOrders.OrderSource"];
export type MeasurementKind = Schemas["OpenTms.Orders.TransportOrders.MeasurementKind"];
export type ApprovalValidationDto = Schemas["OpenTms.Orders.TransportOrders.ApprovalValidationDto"];
export type ApprovalOutcomeDto = Schemas["OpenTms.Orders.TransportOrders.ApprovalOutcomeDto"];
export type OrderApprovalDto = Schemas["OpenTms.Orders.TransportOrders.OrderApprovalDto"];
export type ApprovalResultDto = Schemas["OpenTms.Orders.TransportOrders.ApprovalResultDto"];
export type PricePreviewRequestDto = Schemas["OpenTms.Orders.TransportOrders.PricePreviewRequestDto"];
export type PricePreviewDto = Schemas["OpenTms.Financial.Tariffs.Published.PricePreviewDto"];
export type OrderAttributeDefinitionRow = Schemas["OpenTms.Orders.TransportOrders.OrderAttributeDefinitionRowDto"];
export type OrderAttributeTarget = Schemas["OpenTms.Orders.TransportOrders.OrderAttributeTarget"];
export type TransportOrderTypeRow = Schemas["OpenTms.Orders.TransportOrders.TransportOrderTypeRowDto"];

/** The derived-value target type the order's screens watch (design 56). */
export const TRANSPORT_ORDER_TARGET_TYPE = "Orders.TransportOrder";

/* ── Enum tables (value → state name and localization key) ────────────────────────────────────────────────────── */

export const TRANSPORT_ORDER_STATUSES: { value: TransportOrderStatus; name: string; key: string }[] = [
  { value: 1, name: "Draft", key: "Orders:TransportOrderStatus.Draft" },
  { value: 2, name: "Open", key: "Orders:TransportOrderStatus.Open" },
  { value: 3, name: "ReadyForInvoicing", key: "Orders:TransportOrderStatus.ReadyForInvoicing" },
  { value: 4, name: "Invoiced", key: "Orders:TransportOrderStatus.Invoiced" },
  { value: 5, name: "Archived", key: "Orders:TransportOrderStatus.Archived" },
  { value: 6, name: "Cancelled", key: "Orders:TransportOrderStatus.Cancelled" },
];

/** The state name StatusBadge takes ("Draft", "Open", …) for a status value. */
export function orderStatusName(status: TransportOrderStatus | null | undefined): string | undefined {
  return TRANSPORT_ORDER_STATUSES.find((s) => s.value === status)?.name;
}

export const EXECUTION_PROGRESSES: { value: ExecutionProgress; key: string }[] = [
  { value: 1, key: "Orders:ExecutionProgress.Unplanned" },
  { value: 2, key: "Orders:ExecutionProgress.PartlyPlanned" },
  { value: 3, key: "Orders:ExecutionProgress.Planned" },
  { value: 4, key: "Orders:ExecutionProgress.InProgress" },
  { value: 5, key: "Orders:ExecutionProgress.Delivered" },
];

export const PAYMENT_INSTRUCTIONS: { value: PaymentInstruction; key: string }[] = [
  { value: 0, key: "Orders:PaymentInstruction.NotSet" },
  { value: 1, key: "Orders:PaymentInstruction.Free" },
  { value: 2, key: "Orders:PaymentInstruction.NotFree" },
];

export const ORDER_SOURCES: { value: OrderSource; key: string }[] = [
  { value: 1, key: "Orders:OrderSource.Screen" },
  { value: 2, key: "Orders:OrderSource.Template" },
  { value: 3, key: "Orders:OrderSource.Agent" },
  { value: 4, key: "Orders:OrderSource.Api" },
];

export const MEASUREMENT_KINDS: { value: MeasurementKind; key: string }[] = [
  { value: 1, key: "Orders:MeasurementKind.WeightKg" },
  { value: 2, key: "Orders:MeasurementKind.VolumeM3" },
];

export const enumKey = (items: { value: number; key: string }[], value: number | null | undefined) => items.find((i) => i.value === value)?.key ?? "";

/** The trip request's warning (design 58 build note 19: Planning could not honour the request, the legs stay
 * unplanned) as the localized error code with its `{field}`; null without a warning. */
export function tripRequestWarningText(t: (key: string) => string, request: Pick<TransportOrderTripRequestDto, "warningCode" | "warningField"> | null | undefined): string | null {
  if (!request?.warningCode) return null;
  return t(request.warningCode).replace("{field}", request.warningField ?? "");
}

/** The URL filter values of the Orders list (apps.config.ts, UI-PAGE-004) mapped onto the API's `view` (design 58). */
export const ORDER_VIEWS: Record<string, TransportOrderView> = {
  all: 0,
  today: 1,
  tomorrow: 2,
  "this-week": 3,
  open: 4,
  drafts: 5,
};

/* ── Query keys ──────────────────────────────────────────────────────────────────────────────────────────────── */

export const transportOrderKeys = {
  all: ["orders", "transportOrders"] as const,
  list: () => ["orders", "transportOrders", "list"] as const,
  detail: (id: string | undefined) => ["orders", "transportOrders", "detail", id?.toLowerCase()] as const,
  summary: (id: string | undefined) => ["orders", "transportOrders", "summary", id?.toLowerCase()] as const,
  attributeDefinitions: (target: OrderAttributeTarget) => ["orders", "orderAttributeDefinitions", target] as const,
  orderTypes: () => ["orders", "transportOrderTypes", "rows"] as const,
};

/** The query keys the push refetches for one order: its detail and summary (the list refreshes itself). */
export const transportOrderUpdateKeys = (id: string) => [transportOrderKeys.detail(id), transportOrderKeys.summary(id)];

/* ── List ────────────────────────────────────────────────────────────────────────────────────────────────────── */

export interface TransportOrderListFilters {
  view?: TransportOrderView;
  status?: TransportOrderStatus[];
  dateFrom?: string;
  dateTo?: string;
  customerRelationId?: string;
  organizationUnitId?: string;
  sourceTemplateId?: string;
}

const toPage = <TRow,>(data: { rows?: TRow[] | null; totalCount?: number; filteredCount?: number } | undefined): GridResponse<TRow> => ({
  rows: data?.rows ?? [],
  totalCount: Number(data?.totalCount ?? 0),
  filteredCount: Number(data?.filteredCount ?? 0),
});

export async function fetchTransportOrders(request: GridRequest, filters: TransportOrderListFilters): Promise<GridResponse<TransportOrderRow>> {
  const sort = request.sortModels[0];
  const { data, error } = await apiClient.GET("/api/orders/transport-order", {
    params: {
      query: {
        StartRow: request.startRow,
        EndRow: request.endRow,
        WildcardSearch: request.wildcardSearch ?? undefined,
        SortBy: sort?.colId,
        SortDir: sort?.sort,
        View: filters.view,
        Status: filters.status && filters.status.length > 0 ? filters.status : undefined,
        DateFrom: filters.dateFrom || undefined,
        DateTo: filters.dateTo || undefined,
        CustomerRelationId: filters.customerRelationId || undefined,
        OrganizationUnitId: filters.organizationUnitId || undefined,
        SourceTemplateId: filters.sourceTemplateId || undefined,
      },
    },
  });
  if (error) throw error;
  return toPage<TransportOrderRow>(data);
}

/** The grid fetcher of the Orders list; stable while the filters are the same (the grid refetches on a new one). */
export function useTransportOrdersGridFetcher(filters: TransportOrderListFilters) {
  const key = JSON.stringify(filters);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  return useCallback((request: GridRequest) => fetchTransportOrders(request, JSON.parse(key) as TransportOrderListFilters), [key]);
}

/** Exports the Orders list as xlsx (design 58 build note 19): the list's own request (search, sort, filters) plus the
 * chosen columns and rows; the file is saved under the server's name. A refusal (ExportTooManyRows,
 * ExportUnknownColumn) is thrown as the ABP error. */
export async function exportTransportOrders(request: GridRequest, filters: TransportOrderListFilters, choice: GridExportChoice): Promise<void> {
  const sort = request.sortModels[0];
  const { data, error, response } = await apiClient.POST("/api/orders/transport-order/export", {
    body: {
      wildcardSearch: request.wildcardSearch ?? undefined,
      sortBy: sort?.colId,
      sortDir: sort?.sort,
      view: filters.view,
      status: filters.status && filters.status.length > 0 ? filters.status : undefined,
      dateFrom: filters.dateFrom || undefined,
      dateTo: filters.dateTo || undefined,
      customerRelationId: filters.customerRelationId || undefined,
      organizationUnitId: filters.organizationUnitId || undefined,
      sourceTemplateId: filters.sourceTemplateId || undefined,
      columns: choice.columns,
      allRows: choice.allRows,
    },
    parseAs: "blob",
  });
  if (error) throw error;
  const blob = data as unknown as Blob | undefined;
  if (!blob) throw new Error("The server returned no file.");
  saveBlob(blob, fileNameFromContentDisposition(response.headers.get("Content-Disposition")) ?? fallbackFileName(`transport-orders-${fileDateStamp()}`, response.headers.get("Content-Type") ?? blob.type, ".xlsx"));
}

/* ── Record ──────────────────────────────────────────────────────────────────────────────────────────────────── */

/** Reads one order; the error carries its status (the page tells "not found" from it, recordLoadState). */
export async function fetchTransportOrder(id: string): Promise<TransportOrderDto> {
  const { data, error, response } = await apiClient.GET("/api/orders/transport-order/{id}", { params: { path: { id } } });
  if (error) throw withStatus(error, response);
  return data!;
}

export function useTransportOrder(id: string | undefined) {
  return useQuery({
    queryKey: transportOrderKeys.detail(id),
    enabled: id != null,
    queryFn: () => fetchTransportOrder(id!),
  });
}

export function useTransportOrderSummary(id: string | undefined) {
  return useQuery({
    queryKey: transportOrderKeys.summary(id),
    enabled: id != null,
    queryFn: async (): Promise<TransportOrderSummaryDto> => {
      const { data, error } = await apiClient.GET("/api/orders/transport-order/{id}/summary", { params: { path: { id: id! } } });
      if (error) throw error;
      return data!;
    },
  });
}

/** Watches the orders shown (flow 2): a recalculated order refetches its detail and summary. */
export function useTransportOrderUpdates(ids: readonly (string | null | undefined)[], onUpdate?: () => void) {
  useRecordUpdates(TRANSPORT_ORDER_TARGET_TYPE, ids, { queryKeysFor: transportOrderUpdateKeys, onUpdate });
}

/** The attribute definitions of a target (order or line), for the dynamic attribute fields (design 58, Q12). */
export function useOrderAttributeDefinitions(target: OrderAttributeTarget, enabled = true) {
  return useQuery({
    queryKey: transportOrderKeys.attributeDefinitions(target),
    enabled,
    staleTime: 60 * 1000,
    queryFn: async (): Promise<OrderAttributeDefinitionRow[]> => {
      const { data, error } = await apiClient.GET("/api/orders/order-attribute-definition", {
        params: { query: { Target: target, StartRow: 0, EndRow: 200, SortBy: "position", SortDir: "asc" } },
      });
      if (error) throw error;
      return data?.rows ?? [];
    },
  });
}

/** The tenant's order types with their default flag (New order per type; the default preselected). */
export function useTransportOrderTypeRows(enabled = true) {
  return useQuery({
    queryKey: transportOrderKeys.orderTypes(),
    enabled,
    staleTime: 60 * 1000,
    queryFn: async (): Promise<TransportOrderTypeRow[]> => {
      const { data, error } = await apiClient.GET("/api/orders/transport-order-type", { params: { query: { StartRow: 0, EndRow: 200, IncludeArchived: false } } });
      if (error) throw error;
      return data?.rows ?? [];
    },
  });
}

/* ── Mutations ───────────────────────────────────────────────────────────────────────────────────────────────── */

function useOrderMutation<TArgs>(run: (args: TArgs) => Promise<TransportOrderDto>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: (dto) => {
      if (dto?.id) queryClient.setQueryData(transportOrderKeys.detail(dto.id), dto);
      void queryClient.invalidateQueries({ queryKey: transportOrderKeys.list() });
    },
  });
}

/* The error keeps its HTTP status: the order editor tells a concurrency refusal (409 without a code) from the rest. */
const unwrap = <T,>(result: { data?: T; error?: unknown; response?: Response }): T => {
  if (result.error) throw withStatus(result.error, result.response);
  return result.data as T;
};

export function useCreateTransportOrder() {
  return useOrderMutation(async (dto: TransportOrderCreateDto) => unwrap(await apiClient.POST("/api/orders/transport-order", { body: dto })));
}

export function useUpdateTransportOrderHeader() {
  return useOrderMutation(async ({ id, dto }: { id: string; dto: TransportOrderUpdateDto }) =>
    unwrap(await apiClient.PUT("/api/orders/transport-order/{id}", { params: { path: { id } }, body: dto })),
  );
}

export function useReplaceTransportOrderLines() {
  return useOrderMutation(async ({ id, concurrencyStamp, lines }: { id: string; concurrencyStamp: string; lines: TransportOrderLineInputDto[] }) =>
    unwrap(await apiClient.PUT("/api/orders/transport-order/{id}/lines", { params: { path: { id } }, body: { concurrencyStamp, lines } })),
  );
}

export function useReplaceTransportOrderDefaultStops() {
  return useOrderMutation(async ({ id, concurrencyStamp, defaultStops }: { id: string; concurrencyStamp: string; defaultStops: TransportOrderDefaultStopInputDto[] }) =>
    unwrap(await apiClient.PUT("/api/orders/transport-order/{id}/default-stops", { params: { path: { id } }, body: { concurrencyStamp, defaultStops } })),
  );
}

export function useReplaceTransportOrderReferences() {
  return useOrderMutation(async ({ id, concurrencyStamp, references }: { id: string; concurrencyStamp: string; references: TransportOrderReferenceDto[] }) =>
    unwrap(await apiClient.PUT("/api/orders/transport-order/{id}/references", { params: { path: { id } }, body: { concurrencyStamp, references } })),
  );
}

export function useUpdateTransportOrderFinancialTerms() {
  return useOrderMutation(async ({ id, concurrencyStamp, terms }: { id: string; concurrencyStamp: string; terms: TransportOrderFinancialTermsInputDto }) =>
    unwrap(await apiClient.PUT("/api/orders/transport-order/{id}/financial-terms", { params: { path: { id } }, body: { concurrencyStamp, ...terms } })),
  );
}

export function useUpdateTransportOrderTripRequest() {
  return useOrderMutation(async ({ id, concurrencyStamp, tripRequest }: { id: string; concurrencyStamp: string; tripRequest: TransportOrderTripRequestInputDto | undefined }) =>
    unwrap(await apiClient.PUT("/api/orders/transport-order/{id}/trip-request", { params: { path: { id } }, body: { concurrencyStamp, tripRequest } })),
  );
}

export function useUpdateTransportOrderAttributes() {
  return useOrderMutation(async ({ id, concurrencyStamp, attributes }: { id: string; concurrencyStamp: string; attributes: Record<string, string> }) =>
    unwrap(await apiClient.PUT("/api/orders/transport-order/{id}/attributes", { params: { path: { id } }, body: { concurrencyStamp, attributes } })),
  );
}

export function useConfirmTransportOrder() {
  return useOrderMutation(async (id: string) => unwrap(await apiClient.POST("/api/orders/transport-order/{id}/confirm", { params: { path: { id } } })));
}

export function useCancelTransportOrder() {
  return useOrderMutation(async (id: string) => unwrap(await apiClient.POST("/api/orders/transport-order/{id}/cancel", { params: { path: { id } } })));
}

export function useArchiveTransportOrder() {
  return useOrderMutation(async (id: string) => unwrap(await apiClient.POST("/api/orders/transport-order/{id}/archive", { params: { path: { id } } })));
}

export function useApproveTransportOrder() {
  return useOrderMutation(async ({ id, hash }: { id: string; hash: string }) =>
    unwrap(await apiClient.POST("/api/orders/transport-order/{id}/approve", { params: { path: { id } }, body: { hash } })),
  );
}

export function useReviseTransportOrder() {
  return useOrderMutation(async ({ id, reason }: { id: string; reason: string }) =>
    unwrap(await apiClient.POST("/api/orders/transport-order/{id}/revise", { params: { path: { id } }, body: { reason } })),
  );
}

export function useDeleteTransportOrder() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.DELETE("/api/orders/transport-order/{id}", { params: { path: { id } } });
      if (error) throw error;
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: transportOrderKeys.all }),
  });
}

export function useBulkDeleteTransportOrders() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ selection, filters }: { selection: GridSelectionDto; filters: TransportOrderListFilters }) => {
      const { data, error } = await apiClient.POST("/api/orders/transport-order/bulk-delete", {
        body: {
          selection: {
            mode: selection.mode,
            explicitIds: selection.explicitIds,
            excludedIds: selection.excludedIds,
            filterRequest: selection.filterRequest ?? undefined,
          },
          status: filters.status,
          view: filters.view,
        },
      });
      if (error) throw error;
      return data;
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: transportOrderKeys.all }),
  });
}

/** The approval check of one or more orders: the rule results per order and the hash Approve must echo (design 58, Q6). */
export function useValidateApproval() {
  return useMutation({
    mutationFn: async (ids: string[]): Promise<ApprovalValidationDto> => unwrap(await apiClient.POST("/api/orders/transport-order/validate-approval", { body: { ids } })),
  });
}

export function useApproveMany() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ ids, hash }: { ids: string[]; hash: string }): Promise<ApprovalOutcomeDto> =>
      unwrap(await apiClient.POST("/api/orders/transport-order/approve-many", { body: { ids, hash } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: transportOrderKeys.all }),
  });
}

/** The price preview of an unsaved change (pricing §4.3): pure, nothing is stored. */
export async function previewPrices(id: string, request: PricePreviewRequestDto): Promise<PricePreviewDto> {
  return unwrap(await apiClient.POST("/api/orders/transport-order/{id}/preview-prices", { params: { path: { id } }, body: request }));
}
