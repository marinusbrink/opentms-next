import { useState } from "react";
import { Download, RotateCw } from "lucide-react";
import { toast } from "sonner";

import { useAppFeatures } from "@/app/app-features";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import type { EntityDialogTab, RibbonCommand } from "@/components/ui/entity-dialog";
import { FeatureUnavailable } from "@/components/ui/feature-unavailable";
import { FormSection } from "@/components/ui/form-section";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { RecordLink } from "@/components/ui/record-link";
import { RecordNotFound } from "@/components/ui/record-not-found";
import { RecordPage } from "@/components/ui/record-page";
import { Skeleton } from "@/components/ui/skeleton";
import { StatusBadge } from "@/components/ui/status-badge";
import { IntakeFieldTable } from "@/apps/integrations/components/IntakeFieldTable";
import { StepTimeline } from "@/apps/integrations/components/StepTimeline";
import {
  DOCUMENT_FORMATS,
  INBOUND_CHANNELS,
  INBOUND_DOCUMENT_KINDS,
  INBOUND_DOCUMENT_STATUSES,
  INBOUND_MESSAGE_STATUSES,
  INTAKE_STEPS,
  MAPPING_METHODS,
  downloadInboundDocument,
  downloadInboundMessageRaw,
  enumLabel,
  enumName,
  useInboundMessage,
  useOrderIntakeFeature,
  useReprocessInboundDocument,
  type InboundDocumentDto,
  type InboundMessageDto,
} from "@/domains/integrations/order-intake";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGrantedChecker } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { recordLoadState, recordPath, useCloseRecord } from "@/lib/use-open-record";

/* The trace of one received message (design 72 "UI design"; DEVIATION(UI-PAGE-003): a page route with an id, approved
 * in docs/designs/72-order-intake.md, "UI design": "the trace page /integrations/received/{messageId}"). The record page
 * (RecordPage: brand header, ribbon, tabs) shows the message's header and one tab per document: download the original,
 * format, fingerprint, the profile used (link), the step timeline, and per intake the field table with where each
 * value came from and links to the intake and the order. Reprocess (permission) runs the active document again. This is
 * what a Customer Success Manager sees through impersonation. Page states per UI-PAGE-005. */

export function TracePage({ id }: { id: string }) {
  const { t } = useL();
  const features = useAppFeatures();
  const feature = useOrderIntakeFeature();
  const isGranted = useIsGrantedChecker();
  const canView = isGranted("Integrations.InboundMessages");
  const canReprocess = isGranted("Integrations.InboundMessages.Reprocess");
  const message = useInboundMessage(canView && feature === "on" ? id : undefined);
  const reprocess = useReprocessInboundDocument();
  const closeRecord = useCloseRecord();
  const [activeTab, setActiveTab] = useState<string | undefined>();

  if (feature === "off" || (features.isLoaded && !features.isFeatureEnabled("Apps.Integrations.Received"))) return <FeatureUnavailable />;
  if (!canView) return <PermissionDenied />;
  const state = recordLoadState(message);
  if (feature === "loading" || state === "loading") return <TraceSkeleton />;
  if (state === "not-found") return <RecordNotFound />;
  if (state === "error")
    return (
      <Alert variant="destructive" className="m-6">
        <AlertDescription>{getApiErrorMessage(message.error, t)}</AlertDescription>
      </Alert>
    );

  const dto = message.data!;
  const documents = dto.documents ?? [];
  const current = documents.find((d) => d.id === activeTab) ?? documents[0];

  /* Links into the Transport app are navigation only (an app never imports another); shown when the user may open them. */
  const intakeHref = (intakeId: string) =>
    isGranted("Orders.OrderIntakes") && features.isFeatureEnabled("Apps.Transport.Intake") ? recordPath("OrderIntake", intakeId) : null;
  const orderHref = (orderId: string) =>
    isGranted("Orders.TransportOrders") && features.isFeatureEnabled("Apps.Transport.Orders") ? recordPath("TransportOrder", orderId) : null;
  const profileHref = (profileId: string) =>
    isGranted("Integrations.ImportProfiles") && features.isFeatureEnabled("Apps.MasterData.Integrations.Intake.ImportProfiles")
      ? { to: "/master-data/integrations/import-profiles", search: { open: profileId } }
      : null;

  const download = (run: () => Promise<void>) => run().catch((err: unknown) => toast.error(getApiErrorMessage(err, t)));

  const commands: RibbonCommand[] = [
    ...(canReprocess && current?.id
      ? [
          {
            id: "reprocess",
            labelKey: reprocess.isPending ? "Integrations:Reprocessing" : "Integrations:Reprocess",
            icon: RotateCw,
            isPrimary: true,
            groupKey: "Integrations:Document",
            onClick: () =>
              void reprocess.mutateAsync(current.id!).then(
                () => toast.success(t("Integrations:ReprocessRequested")),
                (err: unknown) => toast.error(getApiErrorMessage(err, t)),
              ),
          },
        ]
      : []),
    ...(dto.hasRawContent
      ? [{ id: "download-message", labelKey: "Integrations:DownloadMessage", icon: Download, groupKey: "Integrations:Message", onClick: () => void download(() => downloadInboundMessageRaw(dto)) }]
      : []),
  ];

  const tabs: EntityDialogTab[] =
    documents.length > 0
      ? documents.map((document, index) => ({
          value: document.id ?? String(index),
          label: document.fileName || enumLabel(t, "InboundDocumentKind", INBOUND_DOCUMENT_KINDS, document.kind),
          content: (
            <DocumentPanel
              message={dto}
              document={document}
              intakeHref={intakeHref}
              orderHref={orderHref}
              profileHref={profileHref}
              onDownload={() => void download(() => downloadInboundDocument(document))}
            />
          ),
        }))
      : [{ value: "message", label: t("Integrations:Message"), content: <MessageHeader message={dto} /> }];

  return (
    <div className="h-full" data-slot="trace-page" data-record-id={dto.id}>
      <RecordPage
        title={`${t("Integrations:InboundMessage")} ${dto.subject || dto.externalId || ""}`.trim()}
        badges={<StatusBadge entity="InboundMessage" state={enumName(INBOUND_MESSAGE_STATUSES, dto.status)} />}
        commands={commands}
        tabs={tabs}
        activeTab={current?.id ?? tabs[0]?.value}
        onActiveTabChange={setActiveTab}
        isBusy={reprocess.isPending}
        onClose={() => closeRecord("InboundMessage")}
      />
    </div>
  );
}

function TraceSkeleton() {
  return (
    <div className="flex h-full flex-col gap-4 p-6" data-slot="record-page-skeleton">
      <Skeleton className="h-10 w-full" />
      <Skeleton className="h-24 w-2/3" />
      <Skeleton className="min-h-0 w-full flex-1" />
    </div>
  );
}

function Detail({ labelKey, children }: { labelKey: string; children: React.ReactNode }) {
  const { t } = useL();
  return (
    <>
      <dt className="text-label font-medium text-muted-foreground">{t(labelKey)}</dt>
      <dd className="min-w-0 break-words">{children}</dd>
    </>
  );
}

/* The message's header: channel, source (mailbox or API client), from, subject, received. */
function MessageHeader({ message }: { message: InboundMessageDto }) {
  const { t } = useL();
  const f = useFormat();
  return (
    <FormSection title={t("Integrations:Message")}>
      <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 text-sm md:grid-cols-[auto_1fr_auto_1fr]">
        <Detail labelKey="Integrations:Channel">{enumLabel(t, "InboundChannel", INBOUND_CHANNELS, message.channel)}</Detail>
        <Detail labelKey="Integrations:Source">{message.sourceName ?? ""}</Detail>
        <Detail labelKey="Integrations:From">{message.from ?? ""}</Detail>
        <Detail labelKey="Integrations:ReceivedAt">
          <span className="tabular-nums">{f.dateTime(message.receivedAt)}</span>
        </Detail>
        <Detail labelKey="Integrations:Subject">{message.subject ?? ""}</Detail>
        <Detail labelKey="Integrations:ExternalId">
          <span className="font-mono text-label break-all">{message.externalId ?? ""}</span>
        </Detail>
      </dl>
    </FormSection>
  );
}

interface DocumentPanelProps {
  message: InboundMessageDto;
  document: InboundDocumentDto;
  intakeHref: (intakeId: string) => string | null;
  orderHref: (orderId: string) => string | null;
  profileHref: (profileId: string) => { to: string; search: Record<string, string> } | null;
  onDownload: () => void;
}

/** The outcome of one intake as the step log tells it (its last review step); the field-level resolution lives in Orders. */
function intakeOutcome(document: InboundDocumentDto, index: number): number | undefined {
  const outcomes = new Set([17, 18, 19, 20, 21]);
  return [...(document.steps ?? [])].reverse().find((step) => step.intakeIndex === index && step.step != null && outcomes.has(step.step))?.step;
}

function DocumentPanel({ message, document, intakeHref, orderHref, profileHref, onDownload }: DocumentPanelProps) {
  const { t } = useL();
  const f = useFormat();
  const profileLink = document.importProfileId ? profileHref(document.importProfileId) : null;
  const profileName = document.importProfileName
    ? `${document.importProfileName}${document.importProfileVersion != null ? ` (${t("Integrations:VersionN").replace("{0}", String(document.importProfileVersion))})` : ""}`
    : null;
  return (
    <div className="grid gap-4" data-slot="trace-document" data-document-id={document.id}>
      <MessageHeader message={message} />
      <FormSection title={t("Integrations:Document")}>
        <div className="grid gap-3">
          <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 text-sm md:grid-cols-[auto_1fr_auto_1fr]">
            <Detail labelKey="Integrations:DocumentKind">{enumLabel(t, "InboundDocumentKind", INBOUND_DOCUMENT_KINDS, document.kind)}</Detail>
            <Detail labelKey="Integrations:Status">
              <StatusBadge entity="InboundDocument" state={enumName(INBOUND_DOCUMENT_STATUSES, document.status)} />
            </Detail>
            <Detail labelKey="Integrations:Format">{enumLabel(t, "DocumentFormat", DOCUMENT_FORMATS, document.format)}</Detail>
            <Detail labelKey="Integrations:Size">
              <span className="tabular-nums">{f.bytes(document.sizeBytes)}</span>
            </Detail>
            <Detail labelKey="Integrations:MappingMethod">{enumLabel(t, "MappingMethod", MAPPING_METHODS, document.mappingMethod)}</Detail>
            <Detail labelKey="Integrations:ProfileUsed">
              {profileName ? profileLink ? <RecordLink to={profileLink.to} search={profileLink.search}>{profileName}</RecordLink> : profileName : <span className="text-muted-foreground">{t("Integrations:NoProfile")}</span>}
            </Detail>
            <Detail labelKey="Integrations:Fingerprint">
              <span className="font-mono text-label break-all">{document.fingerprint ?? ""}</span>
            </Detail>
            <Detail labelKey="Integrations:SenderKey">{document.senderKey ?? ""}</Detail>
          </dl>
          <div>
            <Button variant="outline" size="sm" onClick={onDownload}>
              <Download />
              {t("Integrations:DownloadOriginal")}
            </Button>
          </div>
        </div>
      </FormSection>
      <FormSection title={t("Integrations:Steps")}>
        <StepTimeline steps={document.steps ?? []} intakeHref={intakeHref} orderHref={orderHref} />
      </FormSection>
      {(document.intakes ?? []).map((intake) => {
        const intakeLink = intake.intakeId ? intakeHref(intake.intakeId) : null;
        const orderLink = intake.transportOrderId ? orderHref(intake.transportOrderId) : null;
        const outcome = intakeOutcome(document, intake.index ?? 0);
        return (
          <FormSection key={intake.index} title={t("Integrations:IntakeN").replace("{0}", String((intake.index ?? 0) + 1))}>
            <div className="grid gap-3">
              <div className="flex flex-wrap items-center gap-x-6 gap-y-1 text-sm">
                <span>
                  {t("Integrations:Confidence")}: <span className="tabular-nums">{f.percent((intake.confidence ?? 0) * 100)}</span>
                </span>
                {outcome != null && <span>{enumLabel(t, "IntakeStep", INTAKE_STEPS, outcome)}</span>}
                {intakeLink && <RecordLink to={intakeLink}>{t("Integrations:OpenIntake")}</RecordLink>}
                {orderLink && <RecordLink to={orderLink}>{t("Integrations:OpenOrder").replace("{0}", intake.orderNumber ?? "")}</RecordLink>}
              </div>
              <IntakeFieldTable intake={intake} />
            </div>
          </FormSection>
        );
      })}
    </div>
  );
}
