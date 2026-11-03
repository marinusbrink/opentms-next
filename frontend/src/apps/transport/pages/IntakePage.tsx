import { useMemo, useState } from "react";
import { CheckCheck, CircleX, ClipboardList, FileSearch, Plus } from "lucide-react";
import { useNavigate } from "@tanstack/react-router";
import { toast } from "sonner";

import { useAppFeatures } from "@/app/app-features";
import { Alert, AlertDescription } from "@/components/ui/alert";
import type { EntityDialogTab, RibbonCommand } from "@/components/ui/entity-dialog";
import { FeatureUnavailable } from "@/components/ui/feature-unavailable";
import { FormSection } from "@/components/ui/form-section";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { recordIdentificationTab } from "@/components/ui/record-identification-tab";
import { RecordNotFound } from "@/components/ui/record-not-found";
import { RecordPage } from "@/components/ui/record-page";
import { StatusBadge } from "@/components/ui/status-badge";
import { IntakeAliasesTab } from "@/apps/transport/intake/IntakeAliasesTab";
import { IntakeReview } from "@/apps/transport/intake/IntakeReview";
import { RejectIntakeDialog } from "@/apps/transport/intake/RejectIntakeDialog";
import type { Pick } from "@/apps/transport/intake/ResolutionRow";
import { ORDER_INTAKE_FEATURE, channelLabel, effectiveOverrides, overrideErrors, resolutionByPath, reviewedPayload } from "@/apps/transport/intake/review-model";
import { PageSkeleton } from "@/apps/transport/pages/OrderPage";
import { orderIntakeStatusName, useOrderIntake, useResolveOrderIntake } from "@/domains/orders/order-intakes";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useFeatureState, useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { documentTracePath, recordLoadState, useCloseRecord, useOpenRecord } from "@/lib/use-open-record";

/* The review page of one order intake (design 72 "Review", "UI design"; DEVIATION(UI-PAGE-003): a page route with an id,
 * approved in docs/designs/72-order-intake.md, "UI design": "Review page /transport/intake/{id}"). The values as received,
 * grouped order / lines / stops, each with its resolution and a picker; Create order (a Draft), Create and confirm
 * (needs Orders.TransportOrders.Confirm too, build note 3), Reject with a reason. Every pick on an open field becomes an
 * alias on the server, so the next intake with the same text resolves without a person; the Aliases tab lists them.
 * Page states per UI-PAGE-005; the tenant feature Integrations.OrderIntake gates it (UI-PERM-006). */

export function IntakePage({ id }: { id: string }) {
  const { t } = useL();
  const f = useFormat();
  const features = useAppFeatures();
  const feature = useFeatureState(ORDER_INTAKE_FEATURE);
  const openRecord = useOpenRecord();
  const closeRecord = useCloseRecord();
  const canView = useIsGranted("Orders.OrderIntakes");
  const canResolve = useIsGranted("Orders.OrderIntakes.Resolve");
  const canReject = useIsGranted("Orders.OrderIntakes.Reject");
  const canConfirm = useIsGranted("Orders.TransportOrders.Confirm");
  const canOpenOrder = useIsGranted("Orders.TransportOrders");
  /* View source opens Integrations' trace (navigation only; the Transport app reads nothing from Integrations). */
  const canTrace = useIsGranted("Integrations.InboundMessages") && features.isFeatureEnabled("Apps.Integrations.Received");
  const navigate = useNavigate();
  const detail = useOrderIntake(canView && feature === "on" ? id : undefined);
  const resolve = useResolveOrderIntake();
  const [overrides, setOverrides] = useState<Record<string, string>>({});
  const [picks, setPicks] = useState<Record<string, Pick>>({});
  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [rejecting, setRejecting] = useState(false);

  const dto = detail.data;
  const base = useMemo(() => dto?.reviewedPayload ?? dto?.payload ?? {}, [dto]);
  const resolution = useMemo(() => resolutionByPath(dto?.resolution), [dto?.resolution]);
  const changes = useMemo(() => effectiveOverrides(base, overrides), [base, overrides]);
  const errors = useMemo(() => (submitted ? overrideErrors(changes) : {}), [submitted, changes]);

  if (feature === "off" || (features.isLoaded && !features.isFeatureEnabled("Apps.Transport.Intake"))) return <FeatureUnavailable />;
  if (!canView) return <PermissionDenied />;
  const state = recordLoadState(detail);
  if (feature === "loading" || state === "loading") return <PageSkeleton />;
  if (state === "not-found") return <RecordNotFound />;
  if (state === "error")
    return (
      <Alert variant="destructive" className="m-6">
        <AlertDescription>{getApiErrorMessage(detail.error, t)}</AlertDescription>
      </Alert>
    );

  const intake = dto!;
  const status = orderIntakeStatusName(intake.status);
  const needsReview = status === "NeedsReview";
  const readOnly = !needsReview || !canResolve;
  const isDirty = Object.keys(picks).length > 0 || Object.keys(changes).length > 0;

  const submit = async (confirm: boolean) => {
    setSubmitted(true);
    setError(null);
    if (Object.keys(overrideErrors(changes)).length > 0) return;
    try {
      const result = await resolve.mutateAsync({
        id,
        dto: { picks: Object.fromEntries(Object.entries(picks).map(([path, pick]) => [path, pick.id])), reviewedPayload: reviewedPayload(base, overrides) ?? undefined, confirm },
      });
      setPicks({});
      setOverrides({});
      setSubmitted(false);
      if (orderIntakeStatusName(result.status) === "OrderCreated") {
        toast.success(t(confirm ? "Orders:IntakeOrderCreatedConfirmed" : "Orders:IntakeOrderCreated").replace("{0}", result.orderNumber ?? ""));
        closeRecord("OrderIntake");
      } else {
        setError(t("Orders:IntakeStillOpen"));
      }
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  const commands: RibbonCommand[] = [
    ...(needsReview && canResolve
      ? [
          { id: "create", labelKey: resolve.isPending ? "Orders:IntakeCreating" : "Orders:IntakeCreateOrder", icon: Plus, isPrimary: true, onClick: () => void submit(false) },
          ...(canConfirm ? [{ id: "create-confirm", labelKey: "Orders:IntakeCreateAndConfirm", icon: CheckCheck, onClick: () => void submit(true) }] : []),
        ]
      : []),
    ...(needsReview && canReject ? [{ id: "reject", labelKey: "Orders:Reject", icon: CircleX, variant: "destructive" as const, groupKey: "Orders:IntakeDecision", onClick: () => setRejecting(true) }] : []),
    ...(intake.transportOrderId && canOpenOrder
      ? [{ id: "open-order", labelKey: "Orders:IntakeOpenOrder", icon: ClipboardList, groupKey: "Orders:IntakeDecision", onClick: () => openRecord("TransportOrder", intake.transportOrderId!) }]
      : []),
    ...(canTrace && intake.sourceSummary?.sourceDocumentId
      ? [{ id: "view-source", labelKey: "Orders:IntakeViewSource", icon: FileSearch, groupKey: "Orders:IntakeSource", onClick: () => void navigate({ to: documentTracePath(intake.sourceSummary!.sourceDocumentId!) }) }]
      : []),
  ];

  const source = intake.sourceSummary;
  const summary = (
    <FormSection title={t("Orders:IntakeSource")}>
      <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 text-sm md:grid-cols-[auto_1fr_auto_1fr]">
        <dt className="text-label font-medium text-muted-foreground">{t("Orders:IntakeChannel")}</dt>
        <dd>{channelLabel(t, source?.channel)}</dd>
        <dt className="text-label font-medium text-muted-foreground">{t("Orders:IntakeSender")}</dt>
        <dd className="break-all">{source?.senderAddress ?? ""}</dd>
        <dt className="text-label font-medium text-muted-foreground">{t("Orders:IntakeSubject")}</dt>
        <dd className="break-words">{source?.subject ?? ""}</dd>
        <dt className="text-label font-medium text-muted-foreground">{t("Orders:IntakeFileName")}</dt>
        <dd className="break-all">{source?.fileName ?? ""}</dd>
        <dt className="text-label font-medium text-muted-foreground">{t("Orders:IntakeReceivedAt")}</dt>
        <dd className="tabular-nums">{f.dateTime(intake.creationTime)}</dd>
        <dt className="text-label font-medium text-muted-foreground">{t("Orders:IntakeConfidence")}</dt>
        <dd className="tabular-nums">{f.percent((intake.mappingConfidence ?? 0) * 100)}</dd>
        {intake.orderNumber && (
          <>
            <dt className="text-label font-medium text-muted-foreground">{t("Orders:IntakeOrder")}</dt>
            <dd>{intake.orderNumber}</dd>
          </>
        )}
        {intake.rejectReason && (
          <>
            <dt className="text-label font-medium text-muted-foreground">{t("Orders:RejectReason")}</dt>
            <dd className="break-words">{intake.rejectReason}</dd>
          </>
        )}
      </dl>
    </FormSection>
  );

  const tabs: EntityDialogTab[] = [
    {
      value: "review",
      label: t("Orders:IntakeReviewTab"),
      content: (
        <div className="grid gap-4">
          {error && (
            <Alert variant="destructive">
              <AlertDescription>{error}</AlertDescription>
            </Alert>
          )}
          {needsReview && !canResolve && (
            <Alert variant="warning">
              <AlertDescription>{t("Orders:IntakeReadOnly")}</AlertDescription>
            </Alert>
          )}
          {summary}
          <IntakeReview
            detail={intake}
            base={base}
            overrides={overrides}
            onOverride={(path, text) => setOverrides((current) => ({ ...current, [path]: text }))}
            picks={picks}
            onPick={(path, pick) =>
              setPicks((current) => {
                const next = { ...current };
                if (pick) next[path] = pick;
                else delete next[path];
                return next;
              })
            }
            errors={errors}
            readOnly={readOnly}
            resolution={resolution}
          />
        </div>
      ),
    },
    { value: "aliases", label: t("Orders:IntakeAliases"), content: <IntakeAliasesTab /> },
    recordIdentificationTab(t, { id: intake.id, creationTime: intake.creationTime, lastModificationTime: intake.lastModificationTime }),
  ];

  const title = `${t("Orders:OrderIntake")} ${source?.subject || source?.fileName || source?.senderAddress || ""}`.trim();

  return (
    <div className="h-full" data-slot="intake-page" data-record-id={intake.id} data-status={status}>
      <RecordPage
        title={title}
        badges={<StatusBadge entity="OrderIntake" state={status} />}
        commands={commands}
        tabs={tabs}
        isDirty={isDirty}
        isBusy={resolve.isPending}
        onClose={() => closeRecord("OrderIntake")}
      />
      <RejectIntakeDialog
        intakeId={rejecting ? id : null}
        onOpenChange={setRejecting}
        onRejected={() => {
          setRejecting(false);
          toast.success(t("Orders:IntakeRejected"));
          closeRecord("OrderIntake");
        }}
      />
    </div>
  );
}
