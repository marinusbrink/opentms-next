import { useEffect, useRef } from "react";

import { overlapsRange, type PlanboardRange } from "@/apps/planboard/range";
import type { RecordUpdateMessage } from "@/lib/realtime/record-updates";
import { useRecordTypeUpdates } from "@/lib/realtime/use-record-updates";

/* Live updates of a Planboard pane (design 70, "Live updates"; design 67's record-type channel). A pane watches the
 * record types it shows, decides from each notice's hints whether the record concerns it (date range overlap,
 * organisation unit, a resource in its rows, `removed`) and refetches only those ids, batched: notices arriving within
 * 250 ms reach `onIds` as one list. A reconnect or the window regaining focus calls `onResync`, so the pane refetches
 * its visible page (it may have missed notices meanwhile). */

export const PLANBOARD_RECORD_TYPES = {
  trip: "PlanningExecution.TransportTrip",
  leg: "PlanningExecution.Leg",
  resourceAvailability: "PlanningExecution.ResourceAvailability",
} as const;
export type PlanboardRecordType = (typeof PLANBOARD_RECORD_TYPES)[keyof typeof PLANBOARD_RECORD_TYPES];

export const LIVE_UPDATE_BATCH_MS = 250;

/** What a pane shows, for deciding whether a notice concerns it. Every criterion that is absent accepts. */
export interface PlanboardHintFilter {
  /** The pane's date range: a notice whose from…till does not overlap it is skipped. */
  range?: PlanboardRange | null;
  /** The pane's organisation units (empty or absent = all): a notice for another unit, or for none, is skipped. */
  organizationUnitIds?: readonly string[] | null;
  /** The resources in the pane's rows (a Scheduler's vehicles or drivers): a notice naming none of them is skipped. */
  resourceIds?: readonly string[] | null;
  /** The ids on screen: a notice for one of them always concerns the pane (it must update or drop the row). */
  knownIds?: ReadonlySet<string> | readonly string[] | null;
}

const lower = (id: string) => id.toLowerCase();

function toLowerSet(ids: ReadonlySet<string> | readonly string[] | null | undefined): Set<string> | null {
  if (!ids) return null;
  return new Set([...ids].map(lower));
}

/**
 * Whether a notice concerns a pane with this filter (pure; exported for tests). A record on screen always does; a
 * removed record off screen never does; a notice without hints does (nothing to decide on, refetching is the safe side).
 */
export function hintsConcern(message: RecordUpdateMessage, filter: PlanboardHintFilter): boolean {
  const known = toLowerSet(filter.knownIds);
  if (known?.has(lower(message.targetId))) return true;
  const hints = message.hints;
  if (hints == null) return true;
  if (hints.removed) return false;
  if (filter.range && !overlapsRange(filter.range, hints.from, hints.till)) return false;
  if (filter.organizationUnitIds && filter.organizationUnitIds.length > 0) {
    if (!hints.organizationUnitId) return false;
    const units = new Set(filter.organizationUnitIds.map(lower));
    if (!units.has(lower(hints.organizationUnitId))) return false;
  }
  if (filter.resourceIds) {
    if (!hints.resourceIds || hints.resourceIds.length === 0) return false;
    const resources = new Set(filter.resourceIds.map(lower));
    if (!hints.resourceIds.some((id) => resources.has(lower(id)))) return false;
  }
  return true;
}

export interface UsePlanboardLiveUpdatesOptions {
  /** The record type to watch (one of PLANBOARD_RECORD_TYPES). */
  targetType: PlanboardRecordType | string;
  /** The pane's filter; or a predicate of its own (`filter` is ignored then). */
  filter?: PlanboardHintFilter;
  predicate?: (message: RecordUpdateMessage) => boolean;
  /** The ids (lower case, unique) of the matching notices of one batch, with the notices themselves. */
  onIds: (ids: string[], messages: RecordUpdateMessage[]) => void;
  /** After a reconnect or when the window regains focus: refetch what is visible. */
  onResync?: () => void;
  /** Default true. */
  enabled?: boolean;
  /** Default 250 ms. */
  batchMs?: number;
}

/** Watches one record type for a pane and hands over the ids that concern it, batched (see the file comment). */
export function usePlanboardLiveUpdates({ targetType, filter, predicate, onIds, onResync, enabled = true, batchMs = LIVE_UPDATE_BATCH_MS }: UsePlanboardLiveUpdatesOptions) {
  const latest = useRef({ filter, predicate, onIds, onResync });
  latest.current = { filter, predicate, onIds, onResync };
  const batch = useRef<{ messages: Map<string, RecordUpdateMessage>; timer: ReturnType<typeof setTimeout> | null }>({ messages: new Map(), timer: null });

  const flush = () => {
    const current = batch.current;
    current.timer = null;
    if (current.messages.size === 0) return;
    const messages = [...current.messages.values()];
    current.messages = new Map();
    latest.current.onIds(messages.map((m) => lower(m.targetId)), messages);
  };

  useRecordTypeUpdates(
    targetType,
    (message) => {
      const { filter: f, predicate: p } = latest.current;
      const concerns = p ? p(message) : hintsConcern(message, f ?? {});
      if (!concerns) return;
      /* The last notice per record wins within a batch. */
      batch.current.messages.set(lower(message.targetId), message);
      if (batch.current.timer == null) batch.current.timer = setTimeout(flush, batchMs);
    },
    enabled,
    () => latest.current.onResync?.(),
  );

  /* The window regaining focus (another tab, another app) refetches what is visible. */
  useEffect(() => {
    if (!enabled) return;
    /* A tab switch fires both events; one resync per second is enough. */
    let last = 0;
    const resync = () => {
      const now = Date.now();
      if (now - last < 1000) return;
      last = now;
      latest.current.onResync?.();
    };
    const onVisible = () => {
      if (document.visibilityState === "visible") resync();
    };
    document.addEventListener("visibilitychange", onVisible);
    window.addEventListener("focus", resync);
    return () => {
      document.removeEventListener("visibilitychange", onVisible);
      window.removeEventListener("focus", resync);
    };
  }, [enabled]);

  /* Pending notices are dropped on unmount or when watching stops. */
  useEffect(() => {
    const current = batch.current;
    return () => {
      if (current.timer != null) clearTimeout(current.timer);
      current.timer = null;
      current.messages = new Map();
    };
  }, [targetType, enabled]);
}
