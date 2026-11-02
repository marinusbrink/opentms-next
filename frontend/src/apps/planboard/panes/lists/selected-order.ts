import { useSyncExternalStore } from "react";

/* The order selected in the Transport orders pane (design 70 "Transport orders": "Legs of a selected order show in Legs
 * to schedule's filter 'this order'"). The selection bus carries trips and legs only, so the two list panes share the
 * order through this small store (one Planboard per browser tab; the orders pane clears it when it closes). */

export interface SelectedOrder {
  id: string;
  number: string;
}

let current: SelectedOrder | null = null;
const listeners = new Set<() => void>();

export function setSelectedOrder(order: SelectedOrder | null): void {
  if ((current?.id ?? null) === (order?.id ?? null) && current?.number === order?.number) return;
  current = order;
  listeners.forEach((listener) => listener());
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => void listeners.delete(listener);
}

export function useSelectedOrder(): SelectedOrder | null {
  return useSyncExternalStore(subscribe, () => current, () => null);
}
