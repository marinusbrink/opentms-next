import { useQuery, useQueryClient, type QueryKey } from "@tanstack/react-query";
import { useEffect, useMemo, useRef } from "react";

import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";
import { recordUpdatesClient, type RecordUpdateMessage } from "@/lib/realtime/record-updates";

/* The hooks a domain screen uses for live derived values (design 49): watch the records it shows, refetch what
 * belongs to a record when it was recalculated, and read each record's calculation state for the indicator.
 * useRecordTypeUpdates (design 67) watches a whole record type. */

export type DerivedValueState = components["schemas"]["OpenTms.Platform.DerivedValues.DerivedValueStateDto"];

export const derivedValueStatesKey = (targetType: string) => ["derivedValues", targetType] as const;

export interface UseRecordUpdatesOptions {
  /** The query keys to refetch for a record; default: every query whose key contains the record's id. */
  queryKeysFor?: (id: string) => QueryKey[];
  onUpdate?: (message: RecordUpdateMessage) => void;
}

/** Stable across renders while the set of ids is the same. */
function useIdsKey(ids: readonly (string | null | undefined)[]) {
  const key = [...new Set(ids.filter((id): id is string => !!id).map((id) => id.toLowerCase()))].sort().join(",");
  return useMemo(() => (key ? key.split(",") : []), [key]);
}

export function useRecordUpdates(targetType: string, ids: readonly (string | null | undefined)[], options?: UseRecordUpdatesOptions) {
  const queryClient = useQueryClient();
  const watchedIds = useIdsKey(ids);
  const optionsRef = useRef(options);
  optionsRef.current = options;

  useEffect(() => {
    if (watchedIds.length === 0) return;
    const client = recordUpdatesClient();
    const watched = new Set(watchedIds);

    const refetch = (id: string) => {
      const keys = optionsRef.current?.queryKeysFor?.(id);
      if (keys) keys.forEach((queryKey) => void queryClient.invalidateQueries({ queryKey }));
      else void queryClient.invalidateQueries({ predicate: (q) => q.queryKey.some((part) => typeof part === "string" && part.toLowerCase() === id) });
      void queryClient.invalidateQueries({ queryKey: derivedValueStatesKey(targetType) });
    };

    const unsubscribe = client.subscribe((message) => {
      if (message.targetType !== targetType || !watched.has(message.targetId.toLowerCase())) return;
      refetch(message.targetId.toLowerCase());
      optionsRef.current?.onUpdate?.(message);
    });
    const unReconnect = client.onReconnected(() => watched.forEach(refetch));
    const unwatch = client.watch(targetType, watchedIds);

    return () => {
      unwatch();
      unsubscribe();
      unReconnect();
    };
  }, [queryClient, targetType, watchedIds]);
}

/**
 * Watches every record of a type (design 67), e.g. a planboard pane that must hear about rows it does not know yet.
 * Calls `onUpdate` for every notice of that type (with its routing hints) and `onReconnect` after a reconnect, so the
 * pane refetches what it may have missed. The callbacks may change between renders without re-subscribing.
 */
export function useRecordTypeUpdates(
  targetType: string,
  onUpdate: (message: RecordUpdateMessage) => void,
  enabled = true,
  onReconnect?: () => void,
) {
  const onUpdateRef = useRef(onUpdate);
  onUpdateRef.current = onUpdate;
  const onReconnectRef = useRef(onReconnect);
  onReconnectRef.current = onReconnect;

  useEffect(() => {
    if (!enabled || !targetType) return;
    const client = recordUpdatesClient();
    const unsubscribe = client.subscribe((message) => {
      if (message.targetType === targetType) onUpdateRef.current(message);
    });
    const unReconnect = client.onReconnected(() => onReconnectRef.current?.());
    const unwatch = client.watchType(targetType);

    return () => {
      unwatch();
      unsubscribe();
      unReconnect();
    };
  }, [targetType, enabled]);
}

/** The calculation state per record id (records never calculated are absent); refreshed by the push. */
export function useDerivedValueStates(targetType: string, ids: readonly (string | null | undefined)[]) {
  const stateIds = useIdsKey(ids);
  useRecordUpdates(targetType, stateIds, { queryKeysFor: () => [] });

  return useQuery({
    queryKey: [...derivedValueStatesKey(targetType), stateIds],
    enabled: stateIds.length > 0,
    queryFn: async (): Promise<Map<string, DerivedValueState>> => {
      const { data, error } = await apiClient.GET("/api/platform/derived-value/states", {
        params: { query: { TargetType: targetType, TargetIds: stateIds } },
      });
      if (error) throw error;
      return new Map((data ?? []).map((state) => [String(state.targetId).toLowerCase(), state]));
    },
  });
}
