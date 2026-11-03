import { useId, useState } from "react";
import { ChevronDown, ChevronRight, ClipboardList, Download, FileSearch, RotateCw } from "lucide-react";
import { toast } from "sonner";

import { useAppFeatures } from "@/app/app-features";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import type { EntityDialogTab, RibbonCommand } from "@/components/ui/entity-dialog";
import { FeatureUnavailable } from "@/components/ui/feature-unavailable";
import { FORM_SECTION_CAPTION, FormSection } from "@/components/ui/form-section";
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
import { cn } from "@/lib/utils";
import { recordLoadState, recordPath, useCloseRecord, useOpenRecord } from "@/lib/use-open-record";

/* The trace of one received message (design 72 "UI design"; DEVIATION(UI-PAGE-003): a page route with an id, approved
 * in docs/designs/72-order-intake.md, "UI design": "the trace page /integrations/received/{messageId}"). The record page
 * (RecordPage: brand header, ribbon, tabs) shows one tab per document: the message's header and the document's facts
 * (download the original, format, fingerprint, the profile used as a link), the step timeline, and per intake the field
 * table with where each value came from and links to the intake and the order. Reprocess (permission) runs the active
 * document again. This is what a Customer Success Manager sees through impersonation. Page states per UI-PAGE-005.
 *
 * The page never scrolls (UI-DLG-006, PO 2026-11-03): a document tab starts with one Details card, collapsed by default
 * to a single summary line (channel, mailbox or API client, received, document status, size, import profile) and
 * expanded to the full message and document facts; the steps and the intakes take the rest of the height, side by side
 * from lg and stacked below, each scrolling only its own region. Each list keeps a minimum height (min-h-76); where the
 * tab cannot give both that much (stacked on a short screen, or with the details expanded) the tab panel scrolls, as
 * UI-DLG-006 lets every tab panel do; the page itself never does. The expanded state is component state shared by the
 * document tabs (there is no per-user UI-state store to remember it in).
 *
 * The ribbon's Order intake group (PO 2026-11-03): Review goes to the review page of the message's intake that waits for
 * review (the first of them when there are several: the Transport intake list cannot filter by message), disabled with
 * its reason when none waits; Open order goes to the order when the message produced exactly one. Both are navigation
 * into the Transport app through useOpenRecord, shown only with the permission and the app node of their page. */

/** The last review step of an intake that means it still waits for review (IntakeStep.NeedsReview). */
const NEEDS_REVIEW_STEP = 18;

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
  const openRecord = useOpenRecord();
  const [activeTab, setActiveTab] = useState<string | undefined>();
  const [detailsExpanded, setDetailsExpanded] = useState(false);

  if (feature === "off" || (features.isLoaded && !features.isFeatureEnabled("Apps.Integrations.Intake.Received"))) return <FeatureUnavailable />;
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
  const canOpenIntakes = isGranted("Orders.OrderIntakes") && features.isFeatureEnabled("Apps.Transport.Intake");
  const canOpenOrders = isGranted("Orders.TransportOrders") && features.isFeatureEnabled("Apps.Transport.Orders");
  const intakeHref = (intakeId: string) => (canOpenIntakes ? recordPath("OrderIntake", intakeId) : null);
  const orderHref = (orderId: string) => (canOpenOrders ? recordPath("TransportOrder", orderId) : null);
  const profileHref = (profileId: string) =>
    isGranted("Integrations.ImportProfiles") && features.isFeatureEnabled("Apps.Integrations.Intake.ImportProfiles")
      ? { to: "/integrations/import-profiles", search: { open: profileId } }
      : null;

  /* The message's intakes that wait for review and the orders it produced, over all its documents. */
  const toReview = [
    ...new Set(
      documents.flatMap((document) =>
        (document.intakes ?? [])
          .filter((intake) => intake.intakeId && intakeOutcome(document, intake.index ?? 0) === NEEDS_REVIEW_STEP)
          .map((intake) => intake.intakeId!),
      ),
    ),
  ];
  const orders = [...new Set(documents.flatMap((document) => (document.intakes ?? []).flatMap((intake) => (intake.transportOrderId ? [intake.transportOrderId] : []))))];

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
    ...(canOpenIntakes
      ? [
          {
            id: "review",
            labelKey: "Integrations:ReviewIntake",
            icon: FileSearch,
            groupKey: "Integrations:Nav.OrderIntake",
            disabled: toReview.length === 0,
            disabledReasonKey: "Integrations:NoIntakeToReview",
            onClick: () => void openRecord("OrderIntake", toReview[0]),
          },
        ]
      : []),
    ...(canOpenOrders && orders.length === 1
      ? [{ id: "open-order", labelKey: "Integrations:OpenOrderCommand", icon: ClipboardList, groupKey: "Integrations:Nav.OrderIntake", onClick: () => void openRecord("TransportOrder", orders[0]) }]
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
              detailsExpanded={detailsExpanded}
              onDetailsExpandedChange={setDetailsExpanded}
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
function MessageFacts({ message }: { message: InboundMessageDto }) {
  const { t } = useL();
  const f = useFormat();
  return (
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
  );
}

/* The tab of a message without documents: only its header. */
function MessageHeader({ message }: { message: InboundMessageDto }) {
  const { t } = useL();
  return (
    <FormSection title={t("Integrations:Message")}>
      <MessageFacts message={message} />
    </FormSection>
  );
}

interface DocumentPanelProps {
  message: InboundMessageDto;
  document: InboundDocumentDto;
  intakeHref: (intakeId: string) => string | null;
  orderHref: (orderId: string) => string | null;
  profileHref: (profileId: string) => { to: string; search: Record<string, string> } | null;
  detailsExpanded: boolean;
  onDetailsExpandedChange: (expanded: boolean) => void;
  onDownload: () => void;
}

/** The outcome of one intake as the step log tells it (its last review step); the field-level resolution lives in Orders. */
function intakeOutcome(document: InboundDocumentDto, index: number): number | undefined {
  const outcomes = new Set([17, 18, 19, 20, 21]);
  return [...(document.steps ?? [])].reverse().find((step) => step.intakeIndex === index && step.step != null && outcomes.has(step.step))?.step;
}

interface TraceDetailsProps {
  message: InboundMessageDto;
  document: InboundDocumentDto;
  /** The profile used, already rendered (a link with permission and app node, else its name or "None"). */
  profile: React.ReactNode;
  expanded: boolean;
  onExpandedChange: (expanded: boolean) => void;
  onDownload: () => void;
}

/* The message's and the document's facts in one card (PO 2026-11-03: the two fact cards left the lists 33 px at
 * 1024×768). Collapsed, the default, it is one line: the caption, the toggle and the summary; expanded it shows every
 * fact and Download original. The toggle follows the collapsible Tags panel (EmailTemplateTagsPanel): an icon button
 * with aria-expanded and Expand / Collapse as its name. */
function TraceDetails({ message, document, profile, expanded, onExpandedChange, onDownload }: TraceDetailsProps) {
  const { t } = useL();
  const f = useFormat();
  const id = useId();
  const headingId = `${id}-heading`;
  const factsId = `${id}-facts`;
  const summary: { labelKey: string; value: React.ReactNode }[] = [
    { labelKey: "Integrations:Channel", value: enumLabel(t, "InboundChannel", INBOUND_CHANNELS, message.channel) },
    ...(message.sourceName ? [{ labelKey: "Integrations:Source", value: message.sourceName }] : []),
    { labelKey: "Integrations:ReceivedAt", value: <span className="tabular-nums">{f.dateTime(message.receivedAt)}</span> },
    { labelKey: "Integrations:Status", value: <StatusBadge entity="InboundDocument" state={enumName(INBOUND_DOCUMENT_STATUSES, document.status)} /> },
    { labelKey: "Integrations:Size", value: <span className="tabular-nums">{f.bytes(document.sizeBytes)}</span> },
    { labelKey: "Integrations:ProfileUsed", value: profile },
  ];
  return (
    <section
      aria-labelledby={headingId}
      data-slot="trace-details"
      data-state={expanded ? "expanded" : "collapsed"}
      className="shrink-0 rounded-lg border border-border-section bg-surface-section px-4 py-2"
    >
      <div className="flex min-w-0 items-center gap-2">
        <Button
          type="button"
          variant="ghost"
          size="icon-sm"
          aria-expanded={expanded}
          aria-controls={factsId}
          aria-label={expanded ? t("Collapse") : t("Expand")}
          onClick={() => onExpandedChange(!expanded)}
        >
          {expanded ? <ChevronDown aria-hidden /> : <ChevronRight aria-hidden />}
        </Button>
        <h3 id={headingId} className={cn("shrink-0", FORM_SECTION_CAPTION)}>
          {t("Integrations:Details")}
        </h3>
        {!expanded && (
          <ul data-slot="trace-details-summary" className="ml-2 flex min-w-0 flex-1 flex-wrap items-center gap-x-2 gap-y-1 text-sm">
            {summary.map((item, index) => (
              <li key={item.labelKey} data-summary={item.labelKey} className="flex min-w-0 items-center gap-2">
                {index > 0 && (
                  <span aria-hidden className="text-muted-foreground">
                    ·
                  </span>
                )}
                <span className="sr-only">{t(item.labelKey)}: </span>
                <span className="min-w-0 break-words">{item.value}</span>
              </li>
            ))}
          </ul>
        )}
      </div>
      {expanded && (
        <div id={factsId} data-slot="trace-details-facts" className="grid gap-4 pt-2 pb-2 xl:grid-cols-2">
          <div className="grid content-start gap-2">
            <h4 className="text-label font-semibold text-muted-foreground">{t("Integrations:Message")}</h4>
            <MessageFacts message={message} />
          </div>
          <div className="grid content-start gap-3">
            <h4 className="text-label font-semibold text-muted-foreground">{t("Integrations:Document")}</h4>
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
              <Detail labelKey="Integrations:ProfileUsed">{profile}</Detail>
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
        </div>
      )}
    </section>
  );
}

function DocumentPanel({ message, document, intakeHref, orderHref, profileHref, detailsExpanded, onDetailsExpandedChange, onDownload }: DocumentPanelProps) {
  const { t } = useL();
  const f = useFormat();
  const profileLink = document.importProfileId ? profileHref(document.importProfileId) : null;
  const profileName = document.importProfileName
    ? `${document.importProfileName}${document.importProfileVersion != null ? ` (${t("Integrations:VersionN").replace("{0}", String(document.importProfileVersion))})` : ""}`
    : null;
  const profile = profileName ? (
    profileLink ? (
      <RecordLink to={profileLink.to} search={profileLink.search}>
        {profileName}
      </RecordLink>
    ) : (
      profileName
    )
  ) : (
    <span className="text-muted-foreground">{t("Integrations:NoProfile")}</span>
  );
  const intakes = document.intakes ?? [];
  /* UI-DLG-006: the details card stays at the top, the steps and the intakes share the rest of the tab and scroll only
   * inside their own region. The column is at least the panel's height (min-h-full) and grows only when the lists'
   * minimum (min-h-76 each, a region of about 240 px under the caption) does not fit; then the tab panel scrolls. Every
   * flexible box grows from basis-0, so a long list never adds its own height to the column. */
  return (
    <div className="flex min-h-full flex-col gap-4" data-slot="trace-document" data-document-id={document.id}>
      <TraceDetails
        message={message}
        document={document}
        profile={profile}
        expanded={detailsExpanded}
        onExpandedChange={onDetailsExpandedChange}
        onDownload={onDownload}
      />
      <div data-slot="trace-lists" className="flex grow basis-0 flex-col gap-4 lg:flex-row">
        <FormSection title={t("Integrations:Steps")} className="flex min-h-76 min-w-0 grow basis-0 flex-col">
          <div className="min-h-0 grow basis-0 overflow-y-auto" data-slot="trace-steps">
            <StepTimeline steps={document.steps ?? []} intakeHref={intakeHref} orderHref={orderHref} />
          </div>
        </FormSection>
        {intakes.length > 0 && (
          <FormSection title={t("Integrations:Intakes")} className="flex min-h-76 min-w-0 grow basis-0 flex-col">
            <div className="grid min-h-0 grow basis-0 content-start gap-5 overflow-y-auto" data-slot="trace-intakes">
              {intakes.map((intake) => {
                const intakeLink = intake.intakeId ? intakeHref(intake.intakeId) : null;
                const orderLink = intake.transportOrderId ? orderHref(intake.transportOrderId) : null;
                const outcome = intakeOutcome(document, intake.index ?? 0);
                return (
                  <section key={intake.index} className="grid gap-2" aria-label={t("Integrations:IntakeN").replace("{0}", String((intake.index ?? 0) + 1))}>
                    <div className="flex flex-wrap items-center gap-x-6 gap-y-1 text-sm">
                      <h4 className="font-semibold">{t("Integrations:IntakeN").replace("{0}", String((intake.index ?? 0) + 1))}</h4>
                      <span>
                        {t("Integrations:Confidence")}: <span className="tabular-nums">{f.percent((intake.confidence ?? 0) * 100)}</span>
                      </span>
                      {outcome != null && <span>{enumLabel(t, "IntakeStep", INTAKE_STEPS, outcome)}</span>}
                      {intakeLink && <RecordLink to={intakeLink}>{t("Integrations:OpenIntake")}</RecordLink>}
                      {orderLink && <RecordLink to={orderLink}>{t("Integrations:OpenOrder").replace("{0}", intake.orderNumber ?? "")}</RecordLink>}
                    </div>
                    <IntakeFieldTable intake={intake} />
                  </section>
                );
              })}
            </div>
          </FormSection>
        )}
      </div>
    </div>
  );
}
