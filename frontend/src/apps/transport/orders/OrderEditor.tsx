import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { Archive, CheckCheck, CirclePlay, CircleX, LayoutTemplate, Printer, RefreshCw, RotateCcw, Save, Trash2, TriangleAlert } from "lucide-react";
import { toast } from "sonner";
import { useQueryClient } from "@tanstack/react-query";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import type { EntityDialogTab, RibbonCommand } from "@/components/ui/entity-dialog";
import { RecordPage } from "@/components/ui/record-page";
import { recordIdentificationTab } from "@/components/ui/record-identification-tab";
import { StatusBadge } from "@/components/ui/status-badge";
import { ApproveDialog } from "@/apps/transport/orders/ApproveDialog";
import { DocumentsTab } from "@/apps/transport/orders/DocumentsTab";
import { GeneralTab } from "@/apps/transport/orders/GeneralTab";
import { LinesTab } from "@/apps/transport/orders/LinesTab";
import {
  changedSections,
  contentDiffers,
  customerChanged,
  defaultStopInputOf,
  lineInputOf,
  mergeSavedSections,
  orderValuesOf,
  referencesInputOf,
  savePlan,
  SECTION_LABEL_KEYS,
  templateLineInputOf,
  templateTripRequestInputOf,
  templateValuesOf,
  termsInputOf,
  tripRequestInputOf,
  validateOrder,
  type EditorMode,
  type OrderValues,
  type SectionKey,
} from "@/apps/transport/orders/order-form";
import { PlanningTab } from "@/apps/transport/orders/PlanningTab";
import { PricingTab } from "@/apps/transport/orders/PricingTab";
import { ReviseDialog } from "@/apps/transport/orders/ReviseDialog";
import { MaterialiseWizard } from "@/apps/transport/wizards/MaterialiseWizard";
import { PrintCmrWizard } from "@/apps/transport/wizards/PrintCmrWizard";
import { TripRequestWarningBadge } from "@/apps/transport/components/TripRequestWarning";
import { useOrderRateLineUpdates, useOrderRateLines } from "@/domains/financial/rate-lines";
import { transportOrderTypes } from "@/domains/orders/reference-data";
import { useCreateTemplateFromOrder, useDeleteTemplate, useUpdateTemplate, type TemplateDto } from "@/domains/orders/templates";
import {
  fetchTransportOrder,
  orderStatusName,
  transportOrderKeys,
  useArchiveTransportOrder,
  useCancelTransportOrder,
  useConfirmTransportOrder,
  useDeleteTransportOrder,
  useReplaceTransportOrderDefaultStops,
  useReplaceTransportOrderLines,
  useReplaceTransportOrderReferences,
  useTransportOrderUpdates,
  useUpdateTransportOrderAttributes,
  useUpdateTransportOrderFinancialTerms,
  useUpdateTransportOrderHeader,
  useUpdateTransportOrderTripRequest,
  type TransportOrderDto,
} from "@/domains/orders/transport-orders";
import { getApiErrorMessage, isConcurrencyConflict } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { parseInteger, useFormState } from "@/lib/forms/use-form-state";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useCloseRecord, useOpenRecord } from "@/lib/use-open-record";

/* The order editor (design 61 "Order editor"): a RecordPage with the tabs General, Lines, Pricing, Planning,
 * Documents and Record identification, a ribbon per status (Save, Confirm (Draft), Approve, Revise, Cancel order,
 * Print CMR, Save as template, Delete) with the dirty guard, and the same editor in template mode (Name instead of
 * number, the Schedule section, stops as day offsets; ribbon Save, Create orders, Delete). A save of an order runs the
 * changed sections' commands one after the other with the latest concurrency stamp and returns at once; the derived
 * values arrive by push (flow 2). A header change that brings terms with it sends them in the header call (one call, so
 * a failure cannot save half of it). The API has no transaction over the section commands: a save stops at the first
 * failure, the sections saved so far take the server's values, the rest stay dirty, the page names both (UI-FB-004),
 * and the record is read again so a retry runs with its current concurrency stamp. Document commands save first
 * (UI-ACT-008).
 *
 * Concurrency: the user's edits are based on the server content they started from (`editBase`). When someone else
 * changed that content (a 409 concurrency refusal, or a newer version a push brought in while the form was dirty),
 * the save never adopts the newer stamp for the dirty sections, which would overwrite the other user's change: the
 * page says the order was changed by someone else and offers Reload (discard the unsaved changes, load theirs) or Keep
 * editing (nothing can be saved until a reload), like the Master data editors. A 409 while only derived values changed
 * (the recalculation of the previous save wrote the summary and so the stamp) is no one's change: the step runs again
 * once with the newest stamp. */

/** The tab that shows a field's error (UI-FORM-006: submit switches to the first tab with an error). */
const TAB_OF_FIELD: Record<string, string> = { lines: "lines", terms: "pricing", tripRequest: "planning", defaultStops: "general", schedule: "general", references: "general" };

const DRAFT = 1;
const OPEN = 2;
const READY_FOR_INVOICING = 3;
const INVOICED = 4;
const ARCHIVED = 5;

export interface OrderEditorProps {
  mode: EditorMode;
  order?: TransportOrderDto;
  template?: TemplateDto;
  /** Without the Update permission the editor is read-only (UI-PERM-004). */
  readOnly: boolean;
}

export function OrderEditor({ mode, order, template, readOnly }: OrderEditorProps) {
  const { t } = useL();
  const closeRecord = useCloseRecord();
  const openRecord = useOpenRecord();
  const formRef = useRef<HTMLDivElement>(null);
  const [activeTab, setActiveTab] = useState("general");
  const [serverError, setServerError] = useState<string | null>(null);
  /** After a save that stopped part-way: the sections that were saved and the ones that were not (UI-FB-004). */
  const [saveReport, setSaveReport] = useState<{ saved: SectionKey[]; notSaved: SectionKey[] } | null>(null);
  const queryClient = useQueryClient();
  const clearServerError = () => {
    setServerError(null);
    setSaveReport(null);
  };
  const [dialog, setDialog] = useState<"approve" | "revise" | "print" | "cancel" | "delete" | "materialise" | null>(null);
  /** Someone else changed the order while it was being edited: the prompt (Reload / Keep editing), then the banner. */
  const [conflict, setConflict] = useState<"prompt" | "banner" | null>(null);

  const status = order?.status ?? DRAFT;
  const editableStatus = mode === "template" || status === DRAFT || status === OPEN;
  const formReadOnly = readOnly || !editableStatus;

  const initial = useMemo(() => (mode === "order" ? orderValuesOf(order) : templateValuesOf(template)), [mode, order, template]);
  const validate = useCallback((v: OrderValues) => validateOrder(v, mode), [mode]);
  const { form, isDirty, submit, firstErrorField, reset } = useFormState<OrderValues>(initial, validate, formReadOnly);

  /* The record arrives or changes (a save, a push): the form follows unless the user is editing. */
  const loadedKey = `${order?.id ?? template?.id ?? "new"}|${order?.concurrencyStamp ?? template?.concurrencyStamp ?? ""}`;
  const lastLoadedKey = useRef<string | null>(null);
  /** The server content the unsaved edits are based on; it follows the record only while the form is clean. */
  const editBase = useRef<OrderValues | null>(null);
  useEffect(() => {
    if (lastLoadedKey.current !== loadedKey) {
      const first = lastLoadedKey.current == null;
      lastLoadedKey.current = loadedKey;
      if (first || !isDirty) {
        reset(initial);
        editBase.current = initial;
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loadedKey, initial]);

  const type = transportOrderTypes.useDetail(form.values.transportOrderTypeId ?? undefined);
  const rateLines = useOrderRateLines(mode === "order" ? order?.id : undefined);
  useOrderRateLineUpdates(mode === "order" ? order?.id : undefined);
  useTransportOrderUpdates(mode === "order" ? [order?.id] : []);

  /* Permissions (UI-PERM-003); the design names them (design 56, Q14). */
  const can = {
    confirm: useIsGranted("Orders.TransportOrders.Confirm"),
    cancel: useIsGranted("Orders.TransportOrders.Cancel"),
    approve: useIsGranted("Orders.TransportOrders.Approve"),
    revise: useIsGranted("Orders.TransportOrders.Revise"),
    archive: useIsGranted("Orders.TransportOrders.Archive"),
    print: useIsGranted("Orders.TransportOrders.Print"),
    delete: useIsGranted("Orders.TransportOrders.Delete"),
    template: useIsGranted("Orders.TransportOrderTemplates.Create"),
    deleteTemplate: useIsGranted("Orders.TransportOrderTemplates.Delete"),
    rateLines: useIsGranted("Financial.RateLines.Update"),
  };

  const header = useUpdateTransportOrderHeader();
  const defaultStops = useReplaceTransportOrderDefaultStops();
  const lines = useReplaceTransportOrderLines();
  const references = useReplaceTransportOrderReferences();
  const terms = useUpdateTransportOrderFinancialTerms();
  const tripRequest = useUpdateTransportOrderTripRequest();
  const attributes = useUpdateTransportOrderAttributes();
  const confirm = useConfirmTransportOrder();
  const cancel = useCancelTransportOrder();
  const archive = useArchiveTransportOrder();
  const remove = useDeleteTransportOrder();
  const saveAsTemplate = useCreateTemplateFromOrder();
  const updateTemplate = useUpdateTemplate();
  const removeTemplate = useDeleteTemplate();

  const isSaving = header.isPending || defaultStops.isPending || lines.isPending || references.isPending || terms.isPending || tripRequest.isPending || attributes.isPending || updateTemplate.isPending;
  const isBusy = isSaving || confirm.isPending || cancel.isPending || archive.isPending || remove.isPending || saveAsTemplate.isPending || removeTemplate.isPending;

  /** The newest version of the order (it also refreshes the page's query); null when it cannot be read. */
  const readFresh = (orderId: string) =>
    queryClient.fetchQuery({ queryKey: transportOrderKeys.detail(orderId), queryFn: () => fetchTransportOrder(orderId), staleTime: 0 }).catch(() => null);

  /** Runs the save plan of a stored order (see the header comment); true when every call succeeded. */
  const saveOrder = async (stored: TransportOrderDto, values: OrderValues): Promise<boolean> => {
    const orderId = stored.id!;
    /* A newer version with other content arrived while the form was dirty: saving with its stamp would overwrite it. */
    if (editBase.current && contentDiffers(orderValuesOf(stored), editBase.current)) {
      setConflict("prompt");
      return false;
    }
    let current = stored;
    const stamp = () => current.concurrencyStamp ?? "";
    const run = (command: SectionKey, withTerms: boolean): Promise<TransportOrderDto> => {
      switch (command) {
        case "header":
          return header.mutateAsync({
            id: orderId,
            dto: {
              concurrencyStamp: stamp(),
              transportOrderTypeId: values.transportOrderTypeId!,
              customerRelationId: values.customer!.id,
              customerContactPersonId: values.customerContactPersonId,
              orderDate: values.orderDate,
              customerReference: values.customerReference.trim() || null,
              organizationUnitId: values.organizationUnitId,
              internalRemarks: values.internalRemarks.trim() || null,
              invoicingRemarks: values.invoicingRemarks.trim() || null,
              /* A customer change with the defaults the clerk took over (or the terms kept), or a terms edit together
               * with a header edit: the terms ride along, so the server keeps them as posted (design 58). */
              financialTerms: withTerms ? termsInputOf(values.terms) : undefined,
            },
          });
        case "defaultStops":
          return defaultStops.mutateAsync({ id: orderId, concurrencyStamp: stamp(), defaultStops: values.defaultStops.map(defaultStopInputOf) });
        case "lines":
          return lines.mutateAsync({ id: orderId, concurrencyStamp: stamp(), lines: values.lines.map(lineInputOf) });
        case "references":
          return references.mutateAsync({ id: orderId, concurrencyStamp: stamp(), references: referencesInputOf(values.references) });
        case "terms":
          return terms.mutateAsync({ id: orderId, concurrencyStamp: stamp(), terms: termsInputOf(values.terms) });
        case "tripRequest":
          return tripRequest.mutateAsync({ id: orderId, concurrencyStamp: stamp(), tripRequest: tripRequestInputOf(values.tripRequest) });
        case "attributes":
          return attributes.mutateAsync({ id: orderId, concurrencyStamp: stamp(), attributes: values.attributes });
      }
    };

    const steps = savePlan(changedSections(values, initial), customerChanged(values, initial));
    const saved: SectionKey[] = [];
    /* The saved sections are clean with the server's values; the others keep the input and stay dirty. */
    const keepSaved = () => {
      if (saved.length === 0) return;
      const server = orderValuesOf(current);
      lastLoadedKey.current = `${current.id}|${current.concurrencyStamp ?? ""}`;
      reset(mergeSavedSections(values, server, saved), server);
      editBase.current = server;
    };
    for (const [index, step] of steps.entries()) {
      let retried = false;
      for (;;) {
        try {
          current = await run(step.command, step.sections.includes("terms"));
          saved.push(...step.sections);
          break;
        } catch (err) {
          if (isConcurrencyConflict(err)) {
            /* Whose change was it? The content the server should hold: the base, with the sections saved in this run. */
            const fresh = await readFresh(orderId);
            const expected = editBase.current ? mergeSavedSections(editBase.current, orderValuesOf(current), saved) : null;
            if (!retried && fresh && expected && !contentDiffers(orderValuesOf(fresh), expected)) {
              /* Only derived values changed: no one's content is at stake; run the step again with the newest stamp. */
              retried = true;
              current = fresh;
              continue;
            }
            /* Someone else changed the order: keep the user's input, never send it with the newer stamp. */
            if (steps.length > 1 && saved.length > 0) setSaveReport({ saved: [...saved], notSaved: steps.slice(index).flatMap((s) => s.sections) });
            keepSaved();
            setConflict("prompt");
            return false;
          }
          setServerError(getApiErrorMessage(err, t));
          if (steps.length > 1) setSaveReport({ saved, notSaved: steps.slice(index).flatMap((s) => s.sections) });
          keepSaved();
          /* Read the record again: a retry then runs with its current concurrency stamp (a failed first call may have
           * been a stale one), and the dirty sections are compared with what the server now holds. */
          void queryClient.invalidateQueries({ queryKey: transportOrderKeys.detail(orderId) });
          return false;
        }
      }
    }
    lastLoadedKey.current = `${current.id}|${current.concurrencyStamp ?? ""}`;
    reset(orderValuesOf(current));
    editBase.current = orderValuesOf(current);
    toast.success(t("Orders:TransportOrderSaved"));
    return true;
  };

  /** Reload after a conflict: the unsaved changes are discarded and the other user's version is loaded. */
  const reloadAfterConflict = async () => {
    if (!order?.id) return;
    const fresh = await fetchTransportOrder(order.id);
    queryClient.setQueryData(transportOrderKeys.detail(order.id), fresh);
    const values = orderValuesOf(fresh);
    lastLoadedKey.current = `${fresh.id}|${fresh.concurrencyStamp ?? ""}`;
    reset(values);
    editBase.current = values;
    clearServerError();
    setConflict(null);
    toast.success(t("Orders:TransportOrderReloaded"));
  };

  /** Validates, then runs the changed sections' commands; returns the saved record or null (UI-FORM-006 on failure). */
  const save = async (): Promise<boolean> => {
    if (formReadOnly) return true;
    /* After a conflict nothing is saved until the order is reloaded (no automatic overwrite). */
    if (conflict) {
      setConflict("prompt");
      return false;
    }
    clearServerError();
    const values = submit(formRef.current);
    if (!values) {
      const field = firstErrorField();
      if (field) setActiveTab(TAB_OF_FIELD[field] ?? "general");
      return false;
    }
    if (mode === "order" && order?.id) return saveOrder(order, values);
    try {
      if (mode === "template" && template?.id) {
        const saved = await updateTemplate.mutateAsync({
          id: template.id,
          dto: {
            concurrencyStamp: template.concurrencyStamp ?? "",
            name: values.name.trim(),
            description: values.description.trim() || null,
            isActive: values.isActive,
            transportOrderTypeId: values.transportOrderTypeId!,
            customerRelationId: values.customer!.id,
            customerContactPersonId: values.customerContactPersonId,
            customerReference: values.customerReference.trim() || null,
            organizationUnitId: values.organizationUnitId,
            internalRemarks: values.internalRemarks.trim() || null,
            invoicingRemarks: values.invoicingRemarks.trim() || null,
            attributes: values.attributes,
            defaultStops: values.defaultStops.map(defaultStopInputOf),
            lines: values.lines.map(templateLineInputOf),
            references: referencesInputOf(values.references),
            financialTerms: termsInputOf(values.terms),
            tripRequest: templateTripRequestInputOf(values.tripRequest),
            schedule: {
              recurrence: values.schedule.recurrence,
              interval: parseInteger(values.schedule.interval) ?? 1,
              daysOfWeek: values.schedule.daysOfWeek as 0,
              dayOfMonth: parseInteger(values.schedule.dayOfMonth),
              horizonDays: parseInteger(values.schedule.horizonDays) ?? 5,
              validFrom: values.schedule.validFrom ?? undefined,
              validTill: values.schedule.validTill,
            },
          },
        });
        lastLoadedKey.current = `${saved.id}|${saved.concurrencyStamp ?? ""}`;
        reset(templateValuesOf(saved));
        toast.success(t("Orders:TransportOrderTemplateSaved"));
      }
      return true;
    } catch (err) {
      setServerError(getApiErrorMessage(err, t));
      return false;
    }
  };

  /** Document and state commands save first when the form is dirty (UI-ACT-008). */
  const saveIfDirty = async () => (isDirty ? save() : true);

  const runCommand = async (run: () => Promise<unknown>, successKey: string, closeAfter = false) => {
    clearServerError();
    if (!(await saveIfDirty())) return;
    try {
      await run();
      toast.success(t(successKey));
      if (closeAfter) closeRecord(mode === "order" ? "TransportOrder" : "TransportOrderTemplate");
    } catch (err) {
      setServerError(getApiErrorMessage(err, t));
    }
  };

  const number = order?.number ?? "";
  const title = mode === "template" ? `${t("Orders:TransportOrderTemplate")} ${template?.name ?? ""}` : `${t("Orders:TransportOrder")} ${number || t("Orders:TransportOrderStatus.Draft")}`;

  const commands: RibbonCommand[] = [];
  if (!formReadOnly) commands.push({ id: "save", labelKey: isSaving ? "Saving" : "Save", icon: Save, isPrimary: true, disabled: conflict != null, disabledReasonKey: "Orders:ReloadToSave", onClick: () => void save() });
  if (mode === "order" && order?.id) {
    if (status === DRAFT && can.confirm && !readOnly) commands.push({ id: "confirm", labelKey: "Orders:Confirm", icon: CirclePlay, groupKey: "Orders:Status", onClick: () => void runCommand(() => confirm.mutateAsync(order.id!), "Orders:TransportOrderConfirmed") });
    if (status === OPEN && can.approve) commands.push({ id: "approve", labelKey: "Orders:Approve", icon: CheckCheck, groupKey: "Orders:Status", onClick: () => void saveIfDirty().then((ok) => ok && setDialog("approve")) });
    if ((status === READY_FOR_INVOICING || status === INVOICED) && can.archive) commands.push({ id: "archive", labelKey: "Orders:Archive", icon: Archive, groupKey: "Orders:Status", onClick: () => void runCommand(() => archive.mutateAsync(order.id!), "Orders:TransportOrderArchived") });
    if ((status === READY_FOR_INVOICING || status === ARCHIVED) && can.revise) commands.push({ id: "revise", labelKey: "Orders:Revise", icon: RotateCcw, groupKey: "Orders:Status", onClick: () => setDialog("revise") });
    if ((status === DRAFT || status === OPEN) && can.cancel) commands.push({ id: "cancel", labelKey: "Orders:Cancel", icon: CircleX, variant: "destructive", groupKey: "Orders:Status", onClick: () => setDialog("cancel") });
    if (can.print) commands.push({ id: "print", labelKey: "Orders:PrintConsignmentNote", icon: Printer, groupKey: "Orders:Documents", disabled: (order.lines ?? []).length === 0, disabledReasonKey: "Orders:NoLines", onClick: () => void saveIfDirty().then((ok) => ok && setDialog("print")) });
    if (can.template) commands.push({ id: "template", labelKey: "Orders:SaveAsTemplate", icon: LayoutTemplate, groupKey: "Orders:Documents", onClick: () => void runCommand(async () => {
      const created = await saveAsTemplate.mutateAsync(order.id!);
      if (created.id) openRecord("TransportOrderTemplate", created.id);
    }, "Orders:TransportOrderTemplateSaved") });
    if ((status === DRAFT || status === OPEN) && can.delete) commands.push({ id: "delete", labelKey: "Delete", icon: Trash2, variant: "destructive", groupKey: "Delete", onClick: () => setDialog("delete") });
  }
  if (mode === "template" && template?.id) {
    /* A template without lines would create empty orders (a new one starts without; its lines are added here first). */
    const noLines = form.values.lines.length === 0;
    commands.push({ id: "materialise", labelKey: "Orders:Materialise", icon: CirclePlay, groupKey: "Orders:Documents", disabled: !template.isActive || noLines, disabledReasonKey: !template.isActive ? "OpenTms.Orders:TemplateInactive" : "Orders:TemplateHasNoLines", onClick: () => void saveIfDirty().then((ok) => ok && setDialog("materialise")) });
    if (can.deleteTemplate) commands.push({ id: "delete", labelKey: "Delete", icon: Trash2, variant: "destructive", groupKey: "Delete", onClick: () => setDialog("delete") });
  }

  const sections = useMemo(() => changedSections(form.values, initial), [form.values, initial]);
  const savedReportText = saveReport && saveReport.saved.length > 0 ? `${t("Orders:SectionsSaved").replace("{0}", saveReport.saved.map((s) => t(SECTION_LABEL_KEYS[s])).join(", "))} ` : "";
  const withAlert = (content: ReactNode, tab: string) => (
    <div className="flex h-full min-h-0 flex-col gap-4">
      {conflict && activeTab === tab && (
        <Alert variant="warning" data-slot="order-conflict">
          <TriangleAlert aria-hidden="true" />
          <AlertDescription>
            <p>{t("Orders:OrderChangedByOtherBanner")}</p>
            {saveReport && (
              <p data-slot="save-report">
                {savedReportText}
                {t("Orders:SectionsNotSaved").replace("{0}", saveReport.notSaved.map((s) => t(SECTION_LABEL_KEYS[s])).join(", "))}
              </p>
            )}
            <div className="mt-2">
              <Button type="button" size="sm" variant="outline" onClick={() => setConflict("prompt")}>
                <RefreshCw /> {t("Orders:Reload")}
              </Button>
            </div>
          </AlertDescription>
        </Alert>
      )}
      {serverError && activeTab === tab && (
        <Alert variant="destructive">
          <AlertDescription>
            <p>{serverError}</p>
            {saveReport && (
              <p data-slot="save-report">
                {saveReport.saved.length > 0 && `${t("Orders:SectionsSaved").replace("{0}", saveReport.saved.map((s) => t(SECTION_LABEL_KEYS[s])).join(", "))} `}
                {t("Orders:SectionsNotSaved").replace("{0}", saveReport.notSaved.map((s) => t(SECTION_LABEL_KEYS[s])).join(", "))}
              </p>
            )}
          </AlertDescription>
        </Alert>
      )}
      {content}
    </div>
  );
  const hasError = (fields: (keyof OrderValues & string)[]) => fields.some((field) => form.error(field) != null);

  const tabs: EntityDialogTab[] = [
    { value: "general", label: t("General"), hasError: hasError(["transportOrderTypeId", "customer", "orderDate", "name", "schedule", "defaultStops", "references"]), content: withAlert(<GeneralTab form={form} mode={mode} numbered={!!order?.number} type={type.data} askFinancialDefaults={mode === "order" && order?.id != null} />, "general") },
    { value: "lines", label: t("Orders:Lines"), hasError: hasError(["lines"]), content: withAlert(<LinesTab form={form} mode={mode} type={type.data} rateLines={rateLines.data} />, "lines") },
    { value: "pricing", label: t("Orders:Pricing"), hasError: hasError(["terms"]), content: withAlert(<PricingTab form={form} mode={mode} order={order} rateLines={rateLines.data} previewDirty={sections.lines || sections.terms} canEditRateLines={can.rateLines && editableStatus} />, "pricing") },
    { value: "planning", label: t("Orders:Planning"), hasError: hasError(["tripRequest"]), content: withAlert(<PlanningTab form={form} mode={mode} order={order} />, "planning") },
    ...(mode === "order" ? [{ value: "documents", label: t("Orders:Documents"), content: withAlert(<DocumentsTab orderId={order?.id} onPrint={can.print && (order?.lines ?? []).length > 0 ? () => void saveIfDirty().then((ok) => ok && setDialog("print")) : undefined} />, "documents") }] : []),
    recordIdentificationTab(t, { id: order?.id ?? template?.id, creationTime: order?.creationTime ?? template?.creationTime, lastModificationTime: order?.lastModificationTime ?? template?.lastModificationTime }),
  ];

  const printable = order ? [{ id: order.id!, number: order.number ?? "", lines: (order.lines ?? []).map((l, index) => ({ id: l.id!, position: l.position ?? index + 1, label: [l.transportUnitName, l.productName].filter(Boolean).join(" · ") })) }] : [];

  return (
    <div ref={formRef} className="h-full" data-slot={mode === "order" ? "order-page" : "template-page"} data-record-id={order?.id ?? template?.id} data-status={orderStatusName(order?.status)}>
      <RecordPage
        title={title}
        badges={
          mode === "order" ? (
            <>
              <StatusBadge entity="TransportOrder" state={orderStatusName(order?.status)} />
              <TripRequestWarningBadge request={order?.tripRequest} onOpen={() => setActiveTab("planning")} />
            </>
          ) : undefined
        }
        commands={commands}
        tabs={tabs}
        activeTab={activeTab}
        onActiveTabChange={setActiveTab}
        isDirty={isDirty}
        isBusy={isBusy}
        onClose={() => closeRecord(mode === "order" ? "TransportOrder" : "TransportOrderTemplate")}
      />
      {order?.id && (
        <>
          <ConfirmDialog
            open={conflict === "prompt"}
            onOpenChange={(next) => setConflict(next ? "prompt" : "banner")}
            title={t("Orders:TransportOrderChanged")}
            description={t("Orders:OrderChangedByOther")}
            confirmLabel={t("Orders:Reload")}
            busyLabel={t("Orders:Reloading")}
            cancelLabel={t("Orders:KeepEditing")}
            destructive
            onConfirm={reloadAfterConflict}
          />
          <ApproveDialog ids={[order.id]} open={dialog === "approve"} onOpenChange={(next) => setDialog(next ? "approve" : null)} />
          <ReviseDialog orderId={order.id} number={number} open={dialog === "revise"} onOpenChange={(next) => setDialog(next ? "revise" : null)} />
          <PrintCmrWizard open={dialog === "print"} onOpenChange={(next) => setDialog(next ? "print" : null)} orders={printable} />
          <ConfirmDialog
            open={dialog === "cancel"}
            onOpenChange={(next) => setDialog(next ? "cancel" : null)}
            title={t("Orders:Cancel")}
            description={`${t("Orders:CancelTransportOrderQuestion").replace("{0}", number || t("Orders:TransportOrderStatus.Draft"))} ${t("ThisCannotBeUndone")}`}
            confirmLabel={t("Orders:Cancel")}
            destructive
            onConfirm={async () => {
              setDialog(null);
              await runCommand(() => cancel.mutateAsync(order.id!), "Orders:TransportOrderCancelled");
            }}
          />
          <ConfirmDialog
            open={dialog === "delete"}
            onOpenChange={(next) => setDialog(next ? "delete" : null)}
            title={t("Orders:DeleteTransportOrder")}
            description={`${t("Orders:DeleteTransportOrderQuestion").replace("{0}", number || t("Orders:TransportOrderStatus.Draft"))} ${t("ThisCannotBeUndone")}`}
            confirmLabel={t("Delete")}
            destructive
            onConfirm={async () => {
              setDialog(null);
              clearServerError();
              try {
                await remove.mutateAsync(order.id!);
                toast.success(t("Orders:TransportOrderDeleted"));
                closeRecord("TransportOrder");
              } catch (err) {
                setServerError(getApiErrorMessage(err, t));
              }
            }}
          />
        </>
      )}
      {template?.id && (
        <>
          <MaterialiseWizard open={dialog === "materialise"} onOpenChange={(next) => setDialog(next ? "materialise" : null)} source={{ kind: "template", id: template.id, label: template.name ?? "" }} />
          <ConfirmDialog
            open={dialog === "delete"}
            onOpenChange={(next) => setDialog(next ? "delete" : null)}
            title={t("Orders:DeleteTransportOrderTemplate")}
            description={`${t("Orders:DeleteTransportOrderTemplateQuestion").replace("{0}", template.name ?? "")} ${t("ThisCannotBeUndone")}`}
            confirmLabel={t("Delete")}
            destructive
            onConfirm={async () => {
              setDialog(null);
              clearServerError();
              try {
                await removeTemplate.mutateAsync(template.id!);
                toast.success(t("Orders:TransportOrderTemplateDeleted"));
                closeRecord("TransportOrderTemplate");
              } catch (err) {
                setServerError(getApiErrorMessage(err, t));
              }
            }}
          />
        </>
      )}
    </div>
  );
}
