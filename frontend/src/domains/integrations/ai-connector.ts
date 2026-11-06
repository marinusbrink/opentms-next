import { useCallback } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import type { GridRequest, GridResponse } from "@/components/ui/opentms-grid";
import { apiClient } from "@/lib/api/client";
import { withStatus } from "@/lib/api/errors";
import type { components } from "@/lib/api/generated/schema";

/* The AI connector's administration (design 74, "Connector administration"; PO decision 6): the status with the acceptance
 * of the data sharing, and the connections, each live until someone switches it off. The server decides whose rows a user
 * sees. Accepting, withdrawing and switching off are security actions, so nothing here updates optimistically: the screen
 * changes when the server has answered. */

type Schemas = components["schemas"];

export type AiConnectorStatus = Schemas["OpenTms.Integrations.AiConnector.AiConnectorStatusDto"];
export type AiConnectionRow = Schemas["OpenTms.Integrations.AiConnector.AiConnectionRowDto"];

export const AI_CONNECTOR_USE = "Integrations.AiConnector.Use";
export const AI_CONNECTOR_MANAGE = "Integrations.AiConnector.Manage";

export const aiConnectorKeys = {
  status: ["integrations", "aiConnector", "status"] as const,
};

/** A connection is live until it is switched off (`revokedAt`); a state of the `AiConnection` status badge. */
export function aiConnectionState(row: Pick<AiConnectionRow, "revokedAt">): "Live" | "SwitchedOff" {
  return row.revokedAt == null ? "Live" : "SwitchedOff";
}

const unwrap = <T,>(result: { data?: T; error?: unknown; response?: Response }): T => {
  if (result.error) throw withStatus(result.error, result.response);
  return result.data as T;
};

export function useAiConnectorStatus() {
  return useQuery({
    queryKey: aiConnectorKeys.status,
    queryFn: async () => unwrap(await apiClient.GET("/api/integrations/ai-connector/status")),
  });
}

/** Keeps the server's answer: it carries the new state, also before the setting cache has caught up. */
function useStatusMutation(run: () => Promise<AiConnectorStatus>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: (status) => queryClient.setQueryData(aiConnectorKeys.status, status),
  });
}

export function useAcceptAiConnector() {
  return useStatusMutation(async () => unwrap(await apiClient.POST("/api/integrations/ai-connector/accept")));
}

/** Withdraws the data sharing: every connection of the tenant stops. */
export function useWithdrawAiConnector() {
  return useStatusMutation(async () => unwrap(await apiClient.POST("/api/integrations/ai-connector/withdraw")));
}

/** Switches one connection off; idempotent on the server. */
export function useSwitchOffAiConnection() {
  return useMutation({
    mutationFn: async (id: string) => {
      unwrap(await apiClient.POST("/api/integrations/ai-connector/connections/{id}/revoke", { params: { path: { id } } }));
    },
  });
}

export async function fetchAiConnections(request: GridRequest): Promise<GridResponse<AiConnectionRow>> {
  const sort = request.sortModels[0];
  const { data, error } = await apiClient.GET("/api/integrations/ai-connector/connections", {
    params: { query: { StartRow: request.startRow, EndRow: request.endRow, SortBy: sort?.colId, SortDir: sort?.sort } },
  });
  if (error) throw error;
  return { rows: data?.rows ?? [], totalCount: Number(data?.totalCount ?? 0), filteredCount: Number(data?.filteredCount ?? 0) };
}

/** A grid fetcher that changes when the refresh key does (the grid refetches on a new one). */
export function useAiConnectionsGridFetcher(refreshKey: number) {
  // eslint-disable-next-line react-hooks/exhaustive-deps
  return useCallback((request: GridRequest) => fetchAiConnections(request), [refreshKey]);
}
