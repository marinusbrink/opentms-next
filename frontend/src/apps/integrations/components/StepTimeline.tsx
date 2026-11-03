import { CircleCheck, CircleX, Info, TriangleAlert, type LucideIcon } from "lucide-react";

import { RecordLink } from "@/components/ui/record-link";
import { INTAKE_STEPS, STEP_OUTCOMES, enumLabel, enumName, stepText, type InboundDocumentStep } from "@/domains/integrations/order-intake";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

const OUTCOME_ICONS: Record<string, { icon: LucideIcon; className: string }> = {
  Info: { icon: Info, className: "text-tone-info" },
  Success: { icon: CircleCheck, className: "text-tone-success" },
  Warning: { icon: TriangleAlert, className: "text-tone-warning" },
  Error: { icon: CircleX, className: "text-destructive" },
};

export interface StepTimelineProps {
  steps: InboundDocumentStep[];
  /** The link to an intake's review page, or null when the user may not open it. */
  intakeHref: (intakeId: string) => string | null;
  /** The link to an order's page, or null when the user may not open it. */
  orderHref: (orderId: string) => string | null;
}

/* The step log of one document (design 72 "UI design", the trace): time (technical timestamp, UI-FMT-003), the outcome
 * icon, the step and its localized text with parameters, tokens and cost on AI steps, the user on manual steps, and
 * links to the intake and the order a step names. Oldest first, as the pipeline ran. */
export function StepTimeline({ steps, intakeHref, orderHref }: StepTimelineProps) {
  const { t } = useL();
  const f = useFormat();
  if (steps.length === 0) return <p className="text-sm text-muted-foreground">{t("Integrations:NoSteps")}</p>;
  return (
    <ol className="grid gap-3" data-slot="step-timeline">
      {steps.map((step) => {
        const outcome = enumName(STEP_OUTCOMES, step.outcome) ?? "Info";
        const { icon: Icon, className } = OUTCOME_ICONS[outcome] ?? OUTCOME_ICONS.Info;
        const intakeLink = step.intakeId ? intakeHref(step.intakeId) : null;
        const orderLink = step.transportOrderId ? orderHref(step.transportOrderId) : null;
        const hasUsage = step.inputTokens != null || step.outputTokens != null || step.costMicroUsd != null;
        return (
          <li key={step.id} className="grid grid-cols-[auto_1fr] gap-x-3" data-step={enumName(INTAKE_STEPS, step.step)} data-outcome={outcome}>
            <Icon className={cn("mt-0.5 size-4", className)} aria-label={enumLabel(t, "StepOutcome", STEP_OUTCOMES, step.outcome)} />
            <div className="grid min-w-0 gap-0.5">
              <div className="flex flex-wrap items-baseline gap-x-3">
                <span className="text-sm font-medium">{enumLabel(t, "IntakeStep", INTAKE_STEPS, step.step)}</span>
                <span className="text-label text-muted-foreground tabular-nums">{f.timestamp(step.at)}</span>
                {step.userName && <span className="text-label text-muted-foreground">{t("Integrations:StepBy").replace("{0}", step.userName)}</span>}
              </div>
              <p className="text-sm break-words">{stepText(t, step)}</p>
              {hasUsage && (
                <p className="text-label text-muted-foreground tabular-nums">
                  {t("Integrations:AiUsage")
                    .replace("{model}", step.modelId ?? "")
                    .replace("{input}", f.number(step.inputTokens ?? 0, { decimals: 0 }))
                    .replace("{output}", f.number(step.outputTokens ?? 0, { decimals: 0 }))
                    .replace("{cost}", f.unitPrice((step.costMicroUsd ?? 0) / 1_000_000))}
                </p>
              )}
              {(intakeLink || orderLink) && (
                <p className="flex flex-wrap gap-x-4 text-label">
                  {intakeLink && <RecordLink to={intakeLink}>{t("Integrations:OpenIntake")}</RecordLink>}
                  {orderLink && <RecordLink to={orderLink}>{t("Integrations:OpenOrder").replace("{0}", step.orderNumber ?? "")}</RecordLink>}
                </p>
              )}
            </div>
          </li>
        );
      })}
    </ol>
  );
}
