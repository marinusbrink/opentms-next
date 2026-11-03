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
  /* Design 72 (order intake): the Integrations states (mailboxes, import profiles, received messages and documents)
   * and the Orders ones (the intake, the resolution of one field: green resolved, amber ambiguous, red unresolved). */
  MailboxConnection: {
    AwaitingConsent: { tone: "neutral", labelKey: "Integrations:MailboxStatus.AwaitingConsent" },
    AwaitingFolder: { tone: "warning", labelKey: "Integrations:MailboxStatus.AwaitingFolder" },
    Active: { tone: "success", labelKey: "Integrations:MailboxStatus.Active" },
    Paused: { tone: "neutral", labelKey: "Integrations:MailboxStatus.Paused" },
    Reconnect: { tone: "danger", labelKey: "Integrations:MailboxStatus.Reconnect" },
  },
  ApiClient: {
    Active: { tone: "success", labelKey: "Integrations:ApiClientStatus.Active" },
    Inactive: { tone: "neutral", labelKey: "Integrations:ApiClientStatus.Inactive" },
  },
  ImportProfile: {
    Proposed: { tone: "info", labelKey: "Integrations:ImportProfileStatus.Proposed" },
    Active: { tone: "success", labelKey: "Integrations:ImportProfileStatus.Active" },
    Retired: { tone: "neutral", labelKey: "Integrations:ImportProfileStatus.Retired" },
  },
  InboundMessage: {
    Received: { tone: "neutral", labelKey: "Integrations:InboundMessageStatus.Received" },
    Processing: { tone: "info", labelKey: "Integrations:InboundMessageStatus.Processing" },
    Completed: { tone: "success", labelKey: "Integrations:InboundMessageStatus.Completed" },
    NeedsAttention: { tone: "warning", labelKey: "Integrations:InboundMessageStatus.NeedsAttention" },
    Failed: { tone: "danger", labelKey: "Integrations:InboundMessageStatus.Failed" },
    NotAnOrder: { tone: "neutral", labelKey: "Integrations:InboundMessageStatus.NotAnOrder" },
  },
  InboundDocument: {
    Pending: { tone: "neutral", labelKey: "Integrations:InboundDocumentStatus.Pending" },
    Mapped: { tone: "info", labelKey: "Integrations:InboundDocumentStatus.Mapped" },
    Submitted: { tone: "info", labelKey: "Integrations:InboundDocumentStatus.Submitted" },
    Completed: { tone: "success", labelKey: "Integrations:InboundDocumentStatus.Completed" },
    NeedsReview: { tone: "warning", labelKey: "Integrations:InboundDocumentStatus.NeedsReview" },
    NeedsAttention: { tone: "warning", labelKey: "Integrations:InboundDocumentStatus.NeedsAttention" },
    NotAnOrder: { tone: "neutral", labelKey: "Integrations:InboundDocumentStatus.NotAnOrder" },
    Skipped: { tone: "neutral", labelKey: "Integrations:InboundDocumentStatus.Skipped" },
    Failed: { tone: "danger", labelKey: "Integrations:InboundDocumentStatus.Failed" },
  },
  OrderIntake: {
    NeedsReview: { tone: "warning", labelKey: "Orders:OrderIntakeStatus.NeedsReview" },
    OrderCreated: { tone: "success", labelKey: "Orders:OrderIntakeStatus.OrderCreated" },
    Rejected: { tone: "neutral", labelKey: "Orders:OrderIntakeStatus.Rejected" },
  },
  IntakeResolution: {
    Resolved: { tone: "success", labelKey: "Orders:IntakeResolutionStatus.Resolved" },
    Ambiguous: { tone: "warning", labelKey: "Orders:IntakeResolutionStatus.Ambiguous" },
    Unresolved: { tone: "danger", labelKey: "Orders:IntakeResolutionStatus.Unresolved" },
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
