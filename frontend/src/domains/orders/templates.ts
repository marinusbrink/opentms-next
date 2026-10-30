import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback } from "react";

import type { GridRequest, GridResponse } from "@/components/ui/opentms-grid";
import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";
import { transportOrderKeys } from "@/domains/orders/transport-orders";

/* Transport order templates and recurrence (design 58 §Templates; design 61 "Templates"): the list, the record,
 * CRUD, and the three ways orders come out of a template: `materialise` (the wizard "New from template"),
 * `copy-order` (the wizard "Copy order", a transient template of an existing order) and `from-order` (store an
 * order as a template). The hourly job on the server does the scheduled ones. */

type Schemas = components["schemas"];

export type TemplateRow = Schemas["OpenTms.Orders.TransportOrders.TransportOrderTemplateRowDto"];
export type TemplateDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderTemplateDto"];
export type TemplateCreateDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderTemplateCreateDto"];
export type TemplateUpdateDto = Schemas["OpenTms.Orders.TransportOrders.TransportOrderTemplateUpdateDto"];
export type TemplateLineDto = Schemas["OpenTms.Orders.TransportOrders.TemplateLineDto"];
export type TemplateLineInputDto = Schemas["OpenTms.Orders.TransportOrders.TemplateLineInputDto"];
export type TemplateStopDto = Schemas["OpenTms.Orders.TransportOrders.TemplateStopDto"];
export type TemplateStopInputDto = Schemas["OpenTms.Orders.TransportOrders.TemplateStopInputDto"];
export type TemplateScheduleDto = Schemas["OpenTms.Orders.TransportOrders.TemplateScheduleDto"];
export type TemplateTripRequestInputDto = Schemas["OpenTms.Orders.TransportOrders.TemplateTripRequestInputDto"];
export type TemplateRecurrence = Schemas["OpenTms.Orders.TransportOrders.TemplateRecurrence"];
export type RecurrenceDays = Schemas["OpenTms.Orders.TransportOrders.RecurrenceDays"];
export type MaterialisationResultDto = Schemas["OpenTms.Orders.TransportOrders.MaterialisationResultDto"];
export type MaterialisationOverrideDto = Schemas["OpenTms.Orders.TransportOrders.MaterialisationOverrideDto"];
export type TemplateLookupItem = Schemas["OpenTms.Platform.ReferenceData.ReferenceDataLookupDto"];

export const TEMPLATE_RECURRENCES: { value: TemplateRecurrence; key: string }[] = [
  { value: 0, key: "Orders:TemplateRecurrence.None" },
  { value: 1, key: "Orders:TemplateRecurrence.Weekly" },
  { value: 2, key: "Orders:TemplateRecurrence.Monthly" },
];

/** The [Flags] days of the week of a schedule, Monday first (design 58). */
export const RECURRENCE_DAYS: { flag: number; key: string; /** JS getDay() value */ day: number }[] = [
  { flag: 1, key: "Orders:RecurrenceDays.Monday", day: 1 },
  { flag: 2, key: "Orders:RecurrenceDays.Tuesday", day: 2 },
  { flag: 4, key: "Orders:RecurrenceDays.Wednesday", day: 3 },
  { flag: 8, key: "Orders:RecurrenceDays.Thursday", day: 4 },
  { flag: 16, key: "Orders:RecurrenceDays.Friday", day: 5 },
  { flag: 32, key: "Orders:RecurrenceDays.Saturday", day: 6 },
  { flag: 64, key: "Orders:RecurrenceDays.Sunday", day: 0 },
];

/** The maximum copies per date the server accepts (design 58, `Orders:InvalidCopies`). */
export const MAX_COPIES = 20;

export const templateKeys = {
  all: ["orders", "templates"] as const,
  list: () => ["orders", "templates", "list"] as const,
  detail: (id: string | undefined) => ["orders", "templates", "detail", id?.toLowerCase()] as const,
  lookup: () => ["orders", "templates", "lookup"] as const,
};

export interface TemplateListFilters {
  includeInactive?: boolean;
  customerRelationId?: string;
}

export async function fetchTemplates(request: GridRequest, filters: TemplateListFilters): Promise<GridResponse<TemplateRow>> {
  const sort = request.sortModels[0];
  const { data, error } = await apiClient.GET("/api/orders/transport-order-template", {
    params: {
      query: {
        StartRow: request.startRow,
        EndRow: request.endRow,
        WildcardSearch: request.wildcardSearch ?? undefined,
        SortBy: sort?.colId,
        SortDir: sort?.sort,
        IncludeInactive: filters.includeInactive,
        CustomerRelationId: filters.customerRelationId || undefined,
      },
    },
  });
  if (error) throw error;
  return { rows: data?.rows ?? [], totalCount: Number(data?.totalCount ?? 0), filteredCount: Number(data?.filteredCount ?? 0) };
}

export function useTemplatesGridFetcher(filters: TemplateListFilters) {
  const key = JSON.stringify(filters);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  return useCallback((request: GridRequest) => fetchTemplates(request, JSON.parse(key) as TemplateListFilters), [key]);
}

export function useTemplate(id: string | undefined) {
  return useQuery({
    queryKey: templateKeys.detail(id),
    enabled: id != null,
    queryFn: async (): Promise<TemplateDto> => {
      const { data, error, response } = await apiClient.GET("/api/orders/transport-order-template/{id}", { params: { path: { id: id! } } });
      /* The ABP error body carries no status; the page tells "not found" from it (recordLoadState). */
      if (error) throw { ...(error as object), status: response.status };
      return data!;
    },
  });
}

/** The bounded template lookup (the Source step of the wizard, UI-LOOK-001: a searched list). */
export function useTemplateLookup(enabled = true) {
  return useQuery({
    queryKey: templateKeys.lookup(),
    enabled,
    staleTime: 60 * 1000,
    queryFn: async (): Promise<TemplateLookupItem[]> => {
      const { data, error } = await apiClient.GET("/api/orders/transport-order-template/lookup", { params: { query: { take: 500 } } });
      if (error) throw error;
      return data ?? [];
    },
  });
}

const unwrap = <T,>(result: { data?: T; error?: unknown }): T => {
  if (result.error) throw result.error;
  return result.data as T;
};

export function useCreateTemplate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (dto: TemplateCreateDto): Promise<TemplateDto> => unwrap(await apiClient.POST("/api/orders/transport-order-template", { body: dto })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: templateKeys.all }),
  });
}

export function useUpdateTemplate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, dto }: { id: string; dto: TemplateUpdateDto }): Promise<TemplateDto> =>
      unwrap(await apiClient.PUT("/api/orders/transport-order-template/{id}", { params: { path: { id } }, body: dto })),
    onSuccess: (dto) => {
      if (dto?.id) queryClient.setQueryData(templateKeys.detail(dto.id), dto);
      void queryClient.invalidateQueries({ queryKey: templateKeys.list() });
    },
  });
}

export function useDeleteTemplate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.DELETE("/api/orders/transport-order-template/{id}", { params: { path: { id } } });
      if (error) throw error;
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: templateKeys.all }),
  });
}

/** Stores an order as a template (design 58: `CreateTemplateFromOrder`). */
export function useCreateTemplateFromOrder() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (orderId: string): Promise<TemplateDto> =>
      unwrap(await apiClient.POST("/api/orders/transport-order-template/from-order/{orderId}", { params: { path: { orderId } } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: templateKeys.all }),
  });
}

/** The body of materialise / copy-order: `overrides` (the wizard's per-order details) only when there are any. */
const materialiseBody = (dates: string[], copies: number, overrides?: MaterialisationOverrideDto[]) => (overrides && overrides.length > 0 ? { dates, copies, overrides } : { dates, copies });

/** Creates the orders of the given dates from a template (the wizard "New from template"); `overrides` are the
 * per-order details (customer reference, line quantity and weight) that differ from the template. */
export async function materialiseTemplate(id: string, dates: string[], copies: number, overrides?: MaterialisationOverrideDto[]): Promise<MaterialisationResultDto> {
  return unwrap(await apiClient.POST("/api/orders/transport-order-template/{id}/materialise", { params: { path: { id } }, body: materialiseBody(dates, copies, overrides) }));
}

/** Copies an order onto the given dates through a transient template (the wizard "Copy order"); the overrides name
 * the copied order's line ids. */
export async function copyOrder(orderId: string, dates: string[], copies: number, overrides?: MaterialisationOverrideDto[]): Promise<MaterialisationResultDto> {
  return unwrap(await apiClient.POST("/api/orders/transport-order-template/copy-order/{orderId}", { params: { path: { orderId } }, body: materialiseBody(dates, copies, overrides) }));
}

/** Both wizards end in orders: the Orders list must refresh afterwards. */
export function useInvalidateOrdersAfterMaterialisation() {
  const queryClient = useQueryClient();
  return useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: transportOrderKeys.all });
    void queryClient.invalidateQueries({ queryKey: templateKeys.all });
  }, [queryClient]);
}
