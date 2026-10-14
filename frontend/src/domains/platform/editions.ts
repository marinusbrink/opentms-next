import { useMutation, useQuery } from "@tanstack/react-query";
import { useCallback } from "react";
import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";
import type { GridRequest, GridResponse } from "@/components/ui/opentms-grid";

/* The Editions app (host only): docs/designs/tenants-and-editions.md. Feature values of an edition
 * are ABP's own, under feature-management provider "E" with the edition id as key. */

export type EditionRow = components["schemas"]["OpenTms.Platform.Editions.EditionRowDto"];
export type EditionCreateUpdateDto = components["schemas"]["OpenTms.Platform.Editions.EditionCreateUpdateDto"];
type EditionDeleteCheck = components["schemas"]["OpenTms.Platform.Editions.EditionDeleteCheckDto"];

export const EDITION_FEATURE_PROVIDER = "E";

export type EditionDeleteResult =
  | { status: "deleted" }
  | { status: "conflict"; displayName: string; tenantCount: number };

export function useEditionsGridFetcher(): (request: GridRequest) => Promise<GridResponse<EditionRow>> {
  return useCallback(async (request: GridRequest): Promise<GridResponse<EditionRow>> => {
    const sortModel = request.sortModels[0];
    const { data, error } = await apiClient.GET("/api/platform/editions", {
      params: {
        query: {
          startRow: request.startRow,
          endRow: request.endRow,
          wildcardSearch: request.wildcardSearch ?? undefined,
          sortBy: sortModel?.colId,
          sortDir: sortModel?.sort,
        },
      },
    });
    if (error) throw error;
    return {
      rows: data?.rows ?? [],
      totalCount: Number(data?.totalCount ?? 0),
      filteredCount: Number(data?.filteredCount ?? 0),
    };
  }, []);
}

export function useCreateEdition() {
  return useMutation({
    mutationFn: async (dto: EditionCreateUpdateDto) => {
      const { data, error } = await apiClient.POST("/api/platform/editions", { body: dto });
      if (error) throw error;
      return data!;
    },
  });
}

export function useUpdateEdition() {
  return useMutation({
    mutationFn: async ({ id, dto }: { id: string; dto: EditionCreateUpdateDto }) => {
      const { data, error } = await apiClient.PUT("/api/platform/editions/{id}", {
        params: { path: { id } },
        body: dto,
      });
      if (error) throw error;
      return data!;
    },
  });
}

export function useDeleteEdition() {
  return useMutation({
    mutationFn: async (id: string): Promise<EditionDeleteResult> => {
      const result = await apiClient.DELETE("/api/platform/editions/{id}", { params: { path: { id } } });
      if (result.response.status === 409) {
        const conflict = result.error as EditionDeleteCheck | undefined;
        return { status: "conflict", displayName: conflict?.displayName ?? "", tenantCount: conflict?.tenantCount ?? 0 };
      }
      if (result.error) throw result.error;
      return { status: "deleted" };
    },
  });
}

/* ---- Edition features (ABP feature management, provider "E") ---- */

/** ABP serialises a feature's value type with its concrete shape; the OpenAPI interface type lacks the item source. */
export interface FeatureValueType {
  name?: string | null;
  itemSource?: { items?: { value?: string | null; displayText?: { name?: string | null } | null }[] | null } | null;
}

export interface EditionFeature {
  name: string;
  displayName: string;
  description: string | null;
  depth: number;
  parentName: string | null;
  value: string | null;
  valueType: FeatureValueType | null;
}

export interface EditionFeatureGroup {
  name: string;
  displayName: string;
  features: EditionFeature[];
}

export function useEditionFeatures(editionId: string | undefined) {
  return useQuery({
    queryKey: ["platform", "editions", editionId, "features"],
    enabled: editionId != null,
    queryFn: async (): Promise<EditionFeatureGroup[]> => {
      const { data, error } = await apiClient.GET("/api/feature-management/features", {
        params: { query: { providerName: EDITION_FEATURE_PROVIDER, providerKey: editionId } },
      });
      if (error) throw error;
      return (data?.groups ?? []).map((group) => ({
        name: group.name ?? "",
        displayName: group.displayName ?? group.name ?? "",
        features: (group.features ?? []).map((feature) => ({
          name: feature.name ?? "",
          displayName: feature.displayName ?? feature.name ?? "",
          description: feature.description ?? null,
          depth: feature.depth ?? 0,
          parentName: feature.parentName ?? null,
          value: feature.value ?? null,
          valueType: (feature.valueType as FeatureValueType | undefined) ?? null,
        })),
      }));
    },
  });
}

export function useUpdateEditionFeatures() {
  return useMutation({
    mutationFn: async ({ editionId, values }: { editionId: string; values: { name: string; value: string }[] }) => {
      const { error } = await apiClient.PUT("/api/feature-management/features", {
        params: { query: { providerName: EDITION_FEATURE_PROVIDER, providerKey: editionId } },
        body: { features: values },
      });
      if (error) throw error;
    },
  });
}
