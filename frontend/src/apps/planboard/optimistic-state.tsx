import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

import { fetchPlanboardBars } from "@/domains/planning-execution/planboard-scheduler";
import { legKeys, type LegDto } from "@/domains/planning-execution/legs";
import { transportTripKeys } from "@/domains/planning-execution/trips";
import { apiClient } from "@/lib/api/client";
import { getApiErrorMessage, isConcurrencyConflict, withStatus } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Design 75: optimistic leg assign/unassign state layer. The context tracks per-trip in-flight changes and processes
 * them via a per-trip FIFO command queue. The optimistic state is applied synchronously at pointer-up; the command
 * queue serialises the server calls so each uses the stamp the previous one returned. On error the state is rolled
 * back and a toast explains what happened. */

export interface PlanningOptimisticState {
  /** Legs optimistically added to a trip, keyed by lowercase tripId. */
  assignedLegs: ReadonlyMap<string, ReadonlySet<string>>;
  /** Legs optimistically removed from a trip, keyed by lowercase tripId. */
  unassignedLegs: ReadonlyMap<string, ReadonlySet<string>>;
  /** Trips whose values are locally pending (CalculatingIndicator on), keyed by lowercase tripId. */
  pendingValues: ReadonlySet<string>;
  /** Full leg data for legs being optimistically unassigned, keyed by lowercase legId. */
  pendingLegData: ReadonlyMap<string, LegDto>;
}

const EMPTY_STATE: PlanningOptimisticState = {
  assignedLegs: new Map(),
  unassignedLegs: new Map(),
  pendingValues: new Set(),
  pendingLegData: new Map(),
};

interface TripCommand {
  type: "assign" | "unassign";
  tripId: string;
  legIds: string[];
  fromTripId?: string | null;
  legData?: LegDto[];
  localId: string;
  /** The stamp at the time this command was enqueued; overridden by the queue's running stamp. */
  concurrencyStamp: string;
}

export interface PlanboardOptimisticContext {
  state: PlanningOptimisticState;
  enqueueAssign(params: { tripId: string; legIds: string[]; concurrencyStamp: string; fromTripId?: string | null }): void;
  enqueueUnassign(params: { tripId: string; legIds: string[]; concurrencyStamp: string; legData?: LegDto[] }): void;
  /** Whether the trip has any commands in its queue or is actively processing. */
  isInFlight(tripId: string): boolean;
  /** Reactive set of trips whose command queue is currently processing. Use to defer live updates for in-flight trips. */
  processingTripIds: ReadonlySet<string>;
  /** Called by panes when the SignalR UpToDate notice arrives, to clear the pending indicator. */
  clearPendingValue(tripId: string): void;
}

const Context = createContext<PlanboardOptimisticContext | null>(null);

const lower = (id: string) => id.toLowerCase();

let localIdCounter = 0;
const nextLocalId = () => `opt-${++localIdCounter}`;

type MutableState = {
  assignedLegs: Map<string, Set<string>>;
  unassignedLegs: Map<string, Set<string>>;
  pendingValues: Set<string>;
  pendingLegData: Map<string, LegDto>;
};

function withUpdate(prev: PlanningOptimisticState, updater: (s: MutableState) => void): PlanningOptimisticState {
  const next: MutableState = {
    assignedLegs: new Map([...(prev.assignedLegs as Map<string, Set<string>>)].map(([k, v]) => [k, new Set(v)])),
    unassignedLegs: new Map([...(prev.unassignedLegs as Map<string, Set<string>>)].map(([k, v]) => [k, new Set(v)])),
    pendingValues: new Set(prev.pendingValues),
    pendingLegData: new Map(prev.pendingLegData),
  };
  updater(next);
  return next;
}

export function PlanboardOptimisticStateProvider({ children }: { children: ReactNode }) {
  const { t } = useL();
  const queryClient = useQueryClient();
  const [state, setState] = useState<PlanningOptimisticState>(EMPTY_STATE);
  const [processingTripIds, setProcessingTripIds] = useState<ReadonlySet<string>>(new Set());

  const queues = useRef(new Map<string, TripCommand[]>());
  const processing = useRef(new Set<string>());
  /** Latest confirmed stamp per trip, updated after each successful command. */
  const stamps = useRef(new Map<string, string>());
  const mounted = useRef(true);
  useEffect(() => () => { mounted.current = false; }, []);

  const invalidateAfterCommand = useCallback((tripId: string) => {
    void queryClient.invalidateQueries({ queryKey: transportTripKeys.detail(tripId) });
    void queryClient.invalidateQueries({ queryKey: transportTripKeys.list() });
    void queryClient.invalidateQueries({ queryKey: legKeys.all });
  }, [queryClient]);

  /** Removes the leg from the tracked optimistic set once the server confirmed it. */
  const confirmLegs = useCallback((type: "assign" | "unassign", tripKey: string, legIds: string[]) => {
    setState((prev) =>
      withUpdate(prev, (s) => {
        const map = type === "assign" ? s.assignedLegs : s.unassignedLegs;
        const set = map.get(tripKey);
        if (set) {
          legIds.forEach((id) => {
            set.delete(lower(id));
            if (type === "unassign") s.pendingLegData.delete(lower(id));
          });
          if (set.size === 0) map.delete(tripKey);
        }
      }),
    );
  }, []);

  /** Reverses the optimistic state for the given commands and clears their pendingValues entry. */
  const rollback = useCallback(
    (tripKey: string, commands: TripCommand[], reason: string) => {
      setState((prev) =>
        withUpdate(prev, (s) => {
          for (const cmd of commands) {
            if (cmd.type === "assign") {
              const set = s.assignedLegs.get(tripKey);
              if (set) {
                cmd.legIds.forEach((id) => set.delete(lower(id)));
                if (set.size === 0) s.assignedLegs.delete(tripKey);
              }
              if (cmd.fromTripId) {
                const fromKey = lower(cmd.fromTripId);
                const fromSet = s.unassignedLegs.get(fromKey);
                if (fromSet) {
                  cmd.legIds.forEach((id) => fromSet.delete(lower(id)));
                  if (fromSet.size === 0) s.unassignedLegs.delete(fromKey);
                }
              }
            } else {
              const set = s.unassignedLegs.get(tripKey);
              if (set) {
                cmd.legIds.forEach((id) => {
                  set.delete(lower(id));
                  s.pendingLegData.delete(lower(id));
                });
                if (set.size === 0) s.unassignedLegs.delete(tripKey);
              }
            }
          }
          s.pendingValues.delete(tripKey);
        }),
      );
      queues.current.delete(tripKey);
      toast.error(reason);
    },
    [],
  );

  const executeCommand = useCallback(async (cmd: TripCommand, concurrencyStamp: string) => {
    if (cmd.type === "assign") {
      const result = await apiClient.POST("/api/planning/planboard/{id}/assign-legs", {
        params: { path: { id: cmd.tripId } },
        body: { legIds: cmd.legIds, concurrencyStamp, allowOutsideWindow: true },
      });
      if (result.error) throw withStatus(result.error, result.response);
      return result.data!;
    } else {
      const result = await apiClient.POST("/api/planning/planboard/{id}/unassign-legs", {
        params: { path: { id: cmd.tripId } },
        body: { legIds: cmd.legIds, concurrencyStamp },
      });
      if (result.error) throw withStatus(result.error, result.response);
      return result.data!;
    }
  }, []);

  const processQueue = useCallback(
    async (tripKey: string) => {
      if (processing.current.has(tripKey)) return;
      if (!queues.current.has(tripKey)) return;
      processing.current.add(tripKey);
      setProcessingTripIds((prev) => new Set([...prev, tripKey]));
      try {
        while (mounted.current) {
          const queue = queues.current.get(tripKey);
          if (!queue || queue.length === 0) break;
          const cmd = queue[0];
          const stamp = stamps.current.get(tripKey) ?? cmd.concurrencyStamp;

          let succeeded = false;
          let newStamp: string | undefined;

          try {
            const result = await executeCommand(cmd, stamp);
            newStamp = result.concurrencyStamp ?? undefined;
            succeeded = true;
          } catch (error) {
            if (isConcurrencyConflict(error)) {
              try {
                const fresh = await fetchPlanboardBars([cmd.tripId]);
                const freshStamp = fresh.bars[0]?.concurrencyStamp ?? stamp;
                const result = await executeCommand(cmd, freshStamp);
                newStamp = result.concurrencyStamp ?? undefined;
                succeeded = true;
              } catch (retryError) {
                const remaining = queues.current.get(tripKey) ?? [];
                rollback(
                  tripKey,
                  remaining,
                  isConcurrencyConflict(retryError) ? t("Planboard:Optimistic.StaleStamp") : getApiErrorMessage(retryError, t),
                );
                break;
              }
            } else {
              const remaining = queues.current.get(tripKey) ?? [];
              rollback(tripKey, remaining, getApiErrorMessage(error, t));
              break;
            }
          }

          if (succeeded) {
            if (newStamp) stamps.current.set(tripKey, newStamp);
            confirmLegs(cmd.type, tripKey, cmd.legIds);
            const q = queues.current.get(tripKey);
            if (q) {
              q.shift();
              if (q.length === 0) queues.current.delete(tripKey);
            }
            invalidateAfterCommand(cmd.tripId);
          }
        }
      } finally {
        processing.current.delete(tripKey);
        setProcessingTripIds((prev) => {
          const next = new Set(prev);
          next.delete(tripKey);
          return next;
        });
        /* Commands enqueued while we were processing need a new run. */
        if (mounted.current && queues.current.has(tripKey)) void processQueue(tripKey);
      }
    },
    [t, executeCommand, confirmLegs, rollback, invalidateAfterCommand],
  );

  const enqueueAssign = useCallback(
    ({ tripId, legIds, concurrencyStamp, fromTripId }: { tripId: string; legIds: string[]; concurrencyStamp: string; fromTripId?: string | null }) => {
      const tripKey = lower(tripId);
      stamps.current.set(tripKey, concurrencyStamp);

      /* Optimistic state transition — synchronous, < 100 ms. */
      setState((prev) =>
        withUpdate(prev, (s) => {
          if (!s.assignedLegs.has(tripKey)) s.assignedLegs.set(tripKey, new Set());
          legIds.forEach((id) => s.assignedLegs.get(tripKey)!.add(lower(id)));
          s.pendingValues.add(tripKey);
          if (fromTripId) {
            const fromKey = lower(fromTripId);
            if (!s.unassignedLegs.has(fromKey)) s.unassignedLegs.set(fromKey, new Set());
            legIds.forEach((id) => s.unassignedLegs.get(fromKey)!.add(lower(id)));
            s.pendingValues.add(fromKey);
          }
        }),
      );

      const cmd: TripCommand = { type: "assign", tripId, legIds, fromTripId, localId: nextLocalId(), concurrencyStamp };
      if (!queues.current.has(tripKey)) queues.current.set(tripKey, []);
      queues.current.get(tripKey)!.push(cmd);
      void processQueue(tripKey);
    },
    [processQueue],
  );

  const enqueueUnassign = useCallback(
    ({ tripId, legIds, concurrencyStamp, legData }: { tripId: string; legIds: string[]; concurrencyStamp: string; legData?: LegDto[] }) => {
      const tripKey = lower(tripId);
      if (concurrencyStamp) stamps.current.set(tripKey, concurrencyStamp);

      /* Optimistic state transition — synchronous. */
      setState((prev) =>
        withUpdate(prev, (s) => {
          if (!s.unassignedLegs.has(tripKey)) s.unassignedLegs.set(tripKey, new Set());
          legIds.forEach((id) => s.unassignedLegs.get(tripKey)!.add(lower(id)));
          s.pendingValues.add(tripKey);
          legData?.forEach((leg) => leg.id && s.pendingLegData.set(lower(leg.id), leg));
        }),
      );

      /* Immediately remove legs from the trip's cached legs list (Legs pane shows the cache). */
      const lowerIds = new Set(legIds.map((id) => lower(id)));
      queryClient.setQueryData<LegDto[]>(legKeys.ofTrip(tripId), (current) => current?.filter((leg) => !leg.id || !lowerIds.has(lower(leg.id))));

      const cmd: TripCommand = { type: "unassign", tripId, legIds, legData, localId: nextLocalId(), concurrencyStamp };
      if (!queues.current.has(tripKey)) queues.current.set(tripKey, []);
      queues.current.get(tripKey)!.push(cmd);
      void processQueue(tripKey);
    },
    [processQueue, queryClient],
  );

  const isInFlight = useCallback((tripId: string) => {
    const key = lower(tripId);
    return processing.current.has(key) || queues.current.has(key);
  }, []);

  const clearPendingValue = useCallback((tripId: string) => {
    setState((prev) =>
      withUpdate(prev, (s) => {
        s.pendingValues.delete(lower(tripId));
      }),
    );
  }, []);

  const value: PlanboardOptimisticContext = { state, enqueueAssign, enqueueUnassign, isInFlight, processingTripIds, clearPendingValue };
  return <Context.Provider value={value}>{children}</Context.Provider>;
}

export function usePlanboardOptimisticState(): PlanboardOptimisticContext {
  const ctx = useContext(Context);
  if (!ctx) throw new Error("usePlanboardOptimisticState requires PlanboardOptimisticStateProvider");
  return ctx;
}
