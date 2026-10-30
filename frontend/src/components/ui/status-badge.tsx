import { cn } from "@/lib/utils";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Business states (UI-TOK-005, UI-GRID-004, UI-DLG-012). A state is shown by entity and state
 * name; the registry below says which tone and which label key it gets. The tenant colour pair
 * (App.ColorSettings, D-02) does not exist yet: until it does, every state uses its tone
 * fallback from the --tone-* tokens. An unknown state renders neutral with its raw name, so a
 * new backend state is visible rather than hidden. */

export type StatusTone = "neutral" | "info" | "success" | "warning" | "danger";

interface StatusDefinition {
  tone: StatusTone;
  labelKey: string;
}

const STATUS_REGISTRY: Record<string, Record<string, StatusDefinition>> = {
  TenantProvisioning: {
    Queued: { tone: "neutral", labelKey: "Tenants:State.Queued" },
    Running: { tone: "info", labelKey: "Tenants:State.Running" },
    Succeeded: { tone: "success", labelKey: "Tenants:State.Succeeded" },
    Failed: { tone: "danger", labelKey: "Tenants:State.Failed" },
  },
  /* Design 58 (TransportOrderStatus) and design 59 (TransportTripStatus); the texts live in the Orders and
   * Planning & Execution resources (design 61). */
  TransportOrder: {
    Draft: { tone: "neutral", labelKey: "Orders:Status.Draft" },
    Open: { tone: "info", labelKey: "Orders:Status.Open" },
    ReadyForInvoicing: { tone: "warning", labelKey: "Orders:Status.ReadyForInvoicing" },
    Invoiced: { tone: "success", labelKey: "Orders:Status.Invoiced" },
    Archived: { tone: "neutral", labelKey: "Orders:Status.Archived" },
    Cancelled: { tone: "danger", labelKey: "Orders:Status.Cancelled" },
  },
  TransportTrip: {
    Planned: { tone: "neutral", labelKey: "PlanningExecution:Status.Planned" },
    InProgress: { tone: "info", labelKey: "PlanningExecution:Status.InProgress" },
    Completed: { tone: "success", labelKey: "PlanningExecution:Status.Completed" },
    Cancelled: { tone: "danger", labelKey: "PlanningExecution:Status.Cancelled" },
  },
  /* Design 61 "Order page review" 3: an open rate line of a charged basis without a price list, until it is priced. */
  RateLine: {
    ToBePriced: { tone: "warning", labelKey: "Financial:RateLine.OpenBasis" },
  },
};

const TONE_CLASSES: Record<StatusTone, string> = {
  neutral: "bg-tone-neutral-bg text-tone-neutral",
  info: "bg-tone-info-bg text-tone-info",
  success: "bg-tone-success-bg text-tone-success",
  warning: "bg-tone-warning-bg text-tone-warning",
  danger: "bg-tone-danger-bg text-tone-danger",
};

export interface StatusBadgeProps {
  /** The entity the state belongs to, a key of the status registry (e.g. "TenantProvisioning"). */
  entity: string;
  state: string | null | undefined;
  className?: string;
}

export function StatusBadge({ entity, state, className }: StatusBadgeProps) {
  const { t } = useL();
  if (!state) return null;

  const definition = STATUS_REGISTRY[entity]?.[state];
  const tone = definition?.tone ?? "neutral";
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2 py-0.5 text-label font-medium whitespace-nowrap",
        TONE_CLASSES[tone],
        className,
      )}
      data-tone={tone}
    >
      {definition ? t(definition.labelKey) : state}
    </span>
  );
}

/** Grid cell for a state column (UI-GRID-004): `cellRenderer: StatusCell, cellRendererParams: { entity }`. */
export function StatusCell({ value, entity }: { value: string | null | undefined; entity: string }) {
  return <StatusBadge entity={entity} state={value} />;
}
