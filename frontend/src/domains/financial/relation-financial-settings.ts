import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback } from "react";

import type { GridRequest, GridResponse } from "@/components/ui/opentms-grid";
import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";
import { referenceDataQueryKey } from "@/lib/reference-data/crud-hooks";

/* The financial settings of a relation (design 57, Invoicing): keyed by the Master Data relation, created on the
 * first edit and upserted afterwards with the concurrency stamp. The list follows the design-50 contract (search by
 * relation name through the lookup, build note 15); there is no delete: the settings live as long as the relation. */

type Schemas = components["schemas"];

export type RelationFinancialSettingsRow = Schemas["OpenTms.Financial.Invoicing.RelationFinancialSettingsRowDto"];
export type RelationFinancialSettingsDto = Schemas["OpenTms.Financial.Invoicing.RelationFinancialSettingsDto"];
export type RelationFinancialSettingsUpsertDto = Schemas["OpenTms.Financial.Invoicing.RelationFinancialSettingsUpsertDto"];
export type RelationPriceListDto = Schemas["OpenTms.Financial.Invoicing.RelationPriceListDto"];
export type RelationDiscountOrSurchargeDto = Schemas["OpenTms.Financial.Invoicing.RelationDiscountOrSurchargeDto"];

export const RELATION_FINANCIAL_SETTINGS_ENTITY = "relationFinancialSettings";

/** FinancialConsts: at most 20 lists per side and 50 default discounts or surcharges per relation. */
export const MAX_RELATION_PRICE_LISTS = 20;
export const MAX_RELATION_DISCOUNTS_OR_SURCHARGES = 50;

export function useRelationFinancialSettingsFetcher() {
  return useCallback(async (request: GridRequest): Promise<GridResponse<RelationFinancialSettingsRow>> => {
    const sortModel = request.sortModels[0];
    const { data, error } = await apiClient.GET("/api/financial/relation-financial-settings", {
      params: {
        query: {
          StartRow: request.startRow,
          EndRow: request.endRow,
          WildcardSearch: request.wildcardSearch ?? undefined,
          SortBy: sortModel?.colId,
          SortDir: sortModel?.sort,
        },
      },
    });
    if (error) throw error;
    return { rows: data?.rows ?? [], totalCount: Number(data?.totalCount ?? 0), filteredCount: Number(data?.filteredCount ?? 0) };
  }, []);
}

/** The settings of one relation; `null` when none were saved yet (the editor then starts from the defaults). */
export function useRelationFinancialSettings(relationId: string | undefined) {
  return useQuery({
    queryKey: referenceDataQueryKey(RELATION_FINANCIAL_SETTINGS_ENTITY, "detail", relationId),
    enabled: relationId != null,
    queryFn: async (): Promise<RelationFinancialSettingsDto | null> => {
      const { data, error, response } = await apiClient.GET("/api/financial/relation-financial-settings/by-relation/{relationId}", { params: { path: { relationId: relationId! } } });
      if (error) throw error;
      /* ABP answers an empty 204 for a null result; openapi-fetch then yields undefined. */
      return response.status === 204 || data == null ? null : (data as RelationFinancialSettingsDto);
    },
  });
}

export function useUpsertRelationFinancialSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ relationId, dto }: { relationId: string; dto: RelationFinancialSettingsUpsertDto }): Promise<RelationFinancialSettingsDto> => {
      const { data, error } = await apiClient.POST("/api/financial/relation-financial-settings/upsert-by-relation/{relationId}", { params: { path: { relationId } }, body: dto });
      if (error) throw error;
      return data as RelationFinancialSettingsDto;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: referenceDataQueryKey(RELATION_FINANCIAL_SETTINGS_ENTITY) }),
  });
}
