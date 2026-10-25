import { useCallback, useState, type FormEvent, type ReactNode } from "react";
import { Check, CircleX, LoaderCircle } from "lucide-react";

import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* Multi-step batch operations (UI-WIZ-001 to -005; GAP-04 built in design 61 as option (a): a step list plus a footer,
 * no stepper library). One wizard is one EntityDialog (`variant="action"`, `size="full"`) that this component renders
 * itself, so the footer sits outside the scrolling step content (UI-WIZ-002) and closing goes through the dialog's
 * guard: a wizard with input (`isDirty`) asks before Esc or the backdrop discard it (UI-WIZ-001, UI-DLG-008); while the
 * operation runs the dialog cannot be closed (UI-DLG-010).
 *
 * Steps are linear (UI-WIZ-003): the header numbers them, the current one in brand, completed ones with a check, future
 * ones muted; a completed step can be revisited, a future one not. Next is disabled while the current step is invalid;
 * on the last step it is replaced by the operation verb (UI-ACT-004), destructive when the operation is (UI-ACT-005).
 * Enter advances a valid step (UI-KEY-001: an action dialog submits through its form); it never starts a destructive
 * operation. Once `tasks` is given the run has started (UI-WIZ-004): the body shows the TaskList with one row per item
 * and an overall progress bar, and the footer is a single Close, enabled when every item has finished. No toast per
 * item; a single summary toast after closing is the caller's (UI-FB-002). */

export interface WizardStep {
  id: string;
  /** Already translated. */
  title: string;
  /** False keeps Next (or the verb) disabled (UI-WIZ-002). Default true. */
  isValid?: boolean;
  content: ReactNode;
}

export type WizardTaskStatus = "waiting" | "running" | "done" | "failed";

export interface WizardTask {
  id: string;
  /** Already translated: the item's name (an order number, a relation). */
  label: string;
  status: WizardTaskStatus;
  /** The failure's text (UI-WIZ-004: per-item failures appear in the list). */
  reason?: string;
}

export interface WizardProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Already translated: names the operation ("Generate invoices"). */
  title: string;
  steps: WizardStep[];
  /** The last step's primary: the operation verb, already translated (UI-ACT-004). */
  finishLabel: string;
  /** The verb while the operation is being started (UI-FB-006), e.g. t("Generating"). Defaults to finishLabel. */
  busyLabel?: string;
  /** Starts the operation. A rejected promise keeps the wizard on its last step with the verb enabled again. */
  onFinish: () => void | Promise<void>;
  /** The operation is irreversible: the verb is destructive and Enter never triggers it (UI-ACT-005, UI-KEY-001). */
  destructive?: boolean;
  /**
   * The run (UI-WIZ-004). Undefined before the user starts the operation; once set the body shows the TaskList and the
   * footer a single Close, enabled when every task is done or failed (or `isFinished` says so).
   */
  tasks?: WizardTask[];
  /** Overrides the "every task finished" rule, e.g. for a run that reports its end separately. */
  isFinished?: boolean;
  /** The wizard has input the user would lose (UI-WIZ-001): closing asks first. Defaults to "past the first step". */
  isDirty?: boolean;
  /** Controlled step index; omit to let the wizard manage it. */
  step?: number;
  onStepChange?: (index: number) => void;
}

export function Wizard({
  open,
  onOpenChange,
  title,
  steps,
  finishLabel,
  busyLabel,
  onFinish,
  destructive = false,
  tasks,
  isFinished,
  isDirty,
  step,
  onStepChange,
}: WizardProps) {
  const { t } = useL();
  const [uncontrolledStep, setUncontrolledStep] = useState(0);
  const [starting, setStarting] = useState(false);

  const lastIndex = Math.max(steps.length - 1, 0);
  const current = Math.min(step ?? uncontrolledStep, lastIndex);
  const currentStep = steps[current];
  const isLast = current === lastIndex;
  const isValid = currentStep?.isValid !== false;
  const running = tasks != null;
  const finished = running && (isFinished ?? (tasks.length > 0 && tasks.every((task) => task.status === "done" || task.status === "failed")));

  const goTo = useCallback(
    (index: number) => {
      setUncontrolledStep(index);
      onStepChange?.(index);
    },
    [onStepChange],
  );

  const next = useCallback(() => {
    if (!isValid || starting || running) return;
    if (!isLast) {
      goTo(current + 1);
      return;
    }
    /* Started synchronously, so a click or Enter reaches the caller at once; a failure (thrown or rejected) is the
     * caller's to show (UI-FB-004) and only re-enables the verb here. */
    setStarting(true);
    let result: void | Promise<void>;
    try {
      result = onFinish();
    } catch {
      setStarting(false);
      return;
    }
    Promise.resolve(result)
      .catch(() => undefined)
      .finally(() => setStarting(false));
  }, [current, goTo, isLast, isValid, onFinish, running, starting]);

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault();
    /* UI-KEY-001: Enter advances; it never starts a destructive operation. */
    if (isLast && destructive) return;
    next();
  };

  const footer = running ? (
    <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={!finished}>
      {t("Close")}
    </Button>
  ) : (
    <>
      {/* UI-WIZ-002: Cancel left; Previous and Next / the verb right. The footer is one flex row with the dialog's
       * justify-end, so Cancel takes the left with mr-auto. */}
      <Button type="button" variant="outline" className="mr-auto" onClick={() => onOpenChange(false)} disabled={starting}>
        {t("Cancel")}
      </Button>
      {current > 0 && (
        <Button type="button" variant="outline" onClick={() => goTo(current - 1)} disabled={starting}>
          {t("Wizard:Previous")}
        </Button>
      )}
      <Button
        type="submit"
        form={formId(title)}
        variant={isLast && destructive ? "destructive" : "default"}
        disabled={!isValid || starting}
        onClick={isLast && destructive ? next : undefined}
      >
        {isLast ? (starting ? (busyLabel ?? finishLabel) : finishLabel) : t("Wizard:Next")}
      </Button>
    </>
  );

  return (
    <EntityDialog
      open={open}
      onOpenChange={onOpenChange}
      variant="action"
      size="full"
      title={title}
      isBusy={starting || (running && !finished)}
      isDirty={!running && (isDirty ?? current > 0)}
      footer={footer}
    >
      <div className="flex h-full flex-col gap-4" data-slot="wizard">
        <WizardStepHeader steps={steps} current={current} running={running} onSelect={goTo} />
        {running ? (
          <TaskList tasks={tasks} />
        ) : (
          <form id={formId(title)} onSubmit={handleSubmit} className="flex min-h-0 flex-1 flex-col" data-slot="wizard-step" data-step={currentStep?.id}>
            {currentStep?.content}
          </form>
        )}
      </div>
    </EntityDialog>
  );
}

/** The form id links the footer's submit button to the step form (the footer lives outside the body). */
function formId(title: string): string {
  return `wizard-${title.replace(/[^a-z0-9]+/gi, "-").toLowerCase()}`;
}

interface WizardStepHeaderProps {
  steps: WizardStep[];
  current: number;
  running: boolean;
  onSelect: (index: number) => void;
}

/* UI-WIZ-003: numbered, linear. A completed step is a button back to it; the current and the future ones are not. */
function WizardStepHeader({ steps, current, running, onSelect }: WizardStepHeaderProps) {
  const { t } = useL();
  return (
    <ol className="flex shrink-0 flex-wrap items-center gap-x-6 gap-y-2" aria-label={t("Wizard:Steps")}>
      {steps.map((step, index) => {
        const state = running || index < current ? "completed" : index === current ? "current" : "future";
        const label = t("Wizard:StepOf").replace("{0}", String(index + 1)).replace("{1}", String(steps.length));
        return (
          <li key={step.id} className="flex items-center gap-2" data-state={state} aria-current={state === "current" ? "step" : undefined}>
            <button
              type="button"
              aria-label={`${label}: ${step.title}`}
              disabled={state !== "completed" || running}
              onClick={() => onSelect(index)}
              className={cn(
                "flex items-center gap-2 rounded-md text-sm disabled:cursor-default",
                state === "completed" && "text-foreground hover:underline",
                state === "current" && "font-medium text-foreground",
                state === "future" && "text-muted-foreground",
              )}
            >
              <span
                aria-hidden="true"
                className={cn(
                  "flex size-6 items-center justify-center rounded-full text-label font-semibold",
                  state === "current" && "bg-brand text-brand-foreground",
                  state === "completed" && "bg-tone-success-bg text-tone-success",
                  state === "future" && "bg-muted text-muted-foreground",
                )}
              >
                {state === "completed" ? <Check className="size-4" /> : index + 1}
              </span>
              <span>{step.title}</span>
            </button>
          </li>
        );
      })}
    </ol>
  );
}

export interface TaskListProps {
  tasks: WizardTask[];
}

/* UI-WIZ-004: one row per item with its status, an overall progress bar, failures with their reason in the list. */
export function TaskList({ tasks }: TaskListProps) {
  const { t } = useL();
  const finished = tasks.filter((task) => task.status === "done" || task.status === "failed").length;
  const percent = tasks.length === 0 ? 0 : Math.round((finished / tasks.length) * 100);
  const progressText = t("Wizard:Progress").replace("{0}", String(finished)).replace("{1}", String(tasks.length));

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-3" data-slot="task-list">
      <div className="flex shrink-0 items-center gap-3">
        <div
          role="progressbar"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={percent}
          aria-label={progressText}
          className="h-2 flex-1 overflow-hidden rounded-full bg-muted"
        >
          <div className="h-full bg-primary transition-[width]" style={{ width: `${percent}%` }} />
        </div>
        <span className="text-label text-muted-foreground">{progressText}</span>
      </div>
      <ul className="min-h-0 flex-1 divide-y overflow-y-auto rounded-lg border bg-background">
        {tasks.map((task) => (
          <li key={task.id} className="flex flex-wrap items-start gap-x-3 gap-y-1 px-3 py-2 text-sm" data-status={task.status}>
            <TaskStatusIcon status={task.status} />
            <span className="min-w-0 flex-1 truncate">{task.label}</span>
            <span className={cn("shrink-0 text-label", task.status === "failed" ? "text-tone-danger" : "text-muted-foreground")}>
              {t(TASK_STATUS_KEYS[task.status])}
            </span>
            {task.status === "failed" && task.reason && <span className="basis-full pl-7 text-label text-tone-danger">{task.reason}</span>}
          </li>
        ))}
      </ul>
    </div>
  );
}

const TASK_STATUS_KEYS: Record<WizardTaskStatus, string> = {
  waiting: "Wizard:Task.Waiting",
  running: "Wizard:Task.Running",
  done: "Wizard:Task.Done",
  failed: "Wizard:Task.Failed",
};

function TaskStatusIcon({ status }: { status: WizardTaskStatus }) {
  const className = "mt-0.5 size-4 shrink-0";
  switch (status) {
    case "running":
      return <LoaderCircle className={cn(className, "animate-spin text-tone-info")} aria-hidden="true" />;
    case "done":
      return <Check className={cn(className, "text-tone-success")} aria-hidden="true" />;
    case "failed":
      return <CircleX className={cn(className, "text-tone-danger")} aria-hidden="true" />;
    default:
      return <span className={cn(className, "rounded-full border-2 border-border")} aria-hidden="true" />;
  }
}
