import { lazy, Suspense } from "react";

import type { ResourceSchedulerProps } from "@/components/ui/resource-scheduler";
import { Skeleton } from "@/components/ui/skeleton";

/* The resource scheduler as feature code uses it: lazy-loaded, so FullCalendar (about 300 kB) loads with the first
 * Scheduler only. Types and pure helpers come from resource-scheduler-model.ts. */
const _ResourceScheduler = lazy(() => import("@/components/ui/resource-scheduler").then((m) => ({ default: m.ResourceScheduler })));

export function LazyResourceScheduler<TData = unknown>(props: ResourceSchedulerProps<TData>) {
  return (
    <Suspense fallback={<Skeleton className="h-full w-full" />}>
      <_ResourceScheduler {...(props as ResourceSchedulerProps<unknown>)} />
    </Suspense>
  );
}
