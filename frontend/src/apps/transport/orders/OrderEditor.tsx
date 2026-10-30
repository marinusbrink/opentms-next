import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { Archive, CheckCheck, CirclePlay, CircleX, LayoutTemplate, MessageSquareText, Paperclip, Printer, RefreshCw, RotateCcw, Save, Trash2, TriangleAlert } from "lucide-react";
import { toast } from "sonner";
import { useQueryClient } from "@tanstack/react-query";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import type { EntityDialogTab, RibbonCommand } from "@/components/ui/entity-dialog";
import { NestedTabs } from "@/components/ui/nested-tabs";
import { RecordPage } from "@/components/ui/record-page";
import { recordIdentificationTab } from "@/components/ui/record-identification-tab";
import { StatusBadge } from "@/components/ui/status-badge";
import { AdditionalServices } from "@/apps/transport/orders/AdditionalServices";
import { ApproveDialog } from "@/apps/transport/orders/ApproveDialog";
import { DocumentsTab } from "@/apps/transport/orders/DocumentsTab";
import { FinancialTab } from "@/apps/transport/orders/FinancialTab";
import { FreightItems } from "@/apps/transport/orders/FreightItems";
import { OrderHeader } from "@/apps/transport/orders/OrderHeader";
import { CustomFields, OrderRemarks, OtherReferences, PlannerRemarks, ScheduleSection } from "@/apps/transport/orders/OrderSections";
import { TripRequestSection } from "@/apps/transport/orders/TripRequestSection";
import { usePricePreview } from "@/apps/transport/orders/use-price-preview";
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
  postedLineIdsOf,
  tripLineIdsFor,
  tripLineIdsPosted,
  tripRequestInputOf,
  validateOrder,
  type EditorMode,
  type OrderValues,
  type SectionKey,
} from "@/apps/transport/orders/order-form";
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
  useCreateTransportOrder,
  useDeleteTransportOrder,
  useReplaceTransportOrderDefaultStops,
  useReplaceTransportOrderLines,
  useReplaceTransportOrderReferences,
  useTransportOrderUpdates,
  useUpdateTransportOrderAttributes,
  useUpdateTransportOrderFinancialTerms,
  useUpdateTransportOrderHeader,
  useUpdateTransportOrderTripRequest,
  useOrderAttributeDefinitions,
  type TransportOrderDto,
  type TransportOrderTripRequestInputDto,
} from "@/domains/orders/transport-orders";
import { legKeys, useLegsOfOrder } from "@/domains/planning-execution/legs";
import { getApiErrorMessage, isConcurrencyConflict } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { parseInteger, useFormState } from "@/lib/forms/use-form-state";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useCloseRecord, useOpenRecord } from "@/lib/use-open-record";

/* The order page (design 61 "Order page, legacy tab structure", PO 2026-10-26 and 2026-10-27): the legacy screen's
 * tabs. Main tabs (UI-FORM-008, end-aligned at the right of the ribbon row): General (the header fields, then the
 * nested tabs Freight items, Additional services, Remarks for the planner, Other references and Custom fields when
 * there are order attributes), Financial (the legacy blocks), Tevens trip, Schedule (template only), then the icon
 * tabs Documents (a stored order), Remarks (the order's dated remarks, read-only) and Record identification. A tab
 * that holds an invalid field is marked; a failed submit opens the first tab with an error, nested tab included
 * (UI-FORM-006/007). Freight items are entered on Freight items and edited in the Edit freight item dialog (its own
 * tabs). Templates use the same page in template mode (Name instead of number and date, the Schedule tab, stops as
 * day offsets, no Documents and no Remarks; ribbon Save, Create orders, Delete). A new order is this page without a
 * record (`/transport/orders/new`): the default type, today, the cursor in Customer; Save creates it in one call.
 * Shortcuts the design names (UI-KEY-005): Ctrl/⌘+S saves, Ctrl/⌘+Enter confirms a Draft; they listen on the page
 * (the Edit freight item dialog included), never on the document.
 *
 * A save of a stored order runs the changed sections' commands one after the other with the latest concurrency stamp
 * and returns at once; the derived values arrive by push (flow 2). A header change that brings terms with it sends
 * them in the header call (one call, so a failure cannot save half of it). The API has no transaction over the section
 * commands: a save stops at the first failure, the sections saved so far take the server's values, the rest stay
 * dirty, the page names both (UI-FB-004), and the record is read again so a retry runs with its current concurrency
 * stamp. Document commands save first (UI-ACT-008).
 *
 * Concurrency: the user's edits are based on the server content they started from (`editBase`). When someone else
 * changed that content (a 409 concurrency refusal, or a newer version a push brought in while the form was dirty),
 * the save never adopts the newer stamp for the dirty sections, which would overwrite the other user's change: the
 * page says the order was changed by someone else and offers Reload (discard the unsaved changes, load theirs) or Keep
 * editing (nothing can be saved until a reload), like the Master data editors. A 409 while only derived values changed
 * (the recalculation of the previous save wrote the summary and so the stamp) is no one's change: the step runs again
 * once with the newest stamp. */

type MainTab = "general" | "financial" | "trip" | "schedule" | "documents" | "remarks" | "identification";
type GeneralTab = "items" | "services" | "plannerRemarks" | "references" | "custom";

/** The tab (and the nested tab of General) that shows a field's error (UI-FORM-006: submit opens the first tab with
 * an error); every field not named here is on General's header. */
const TAB_OF_FIELD: Partial<Record<string, [MainTab, GeneralTab?]>> = {
  lines: ["general", "items"],
  references: ["general", "references"],
  attributes: ["general", "custom"],
  terms: ["financial"],
  tripRequest: ["trip"],
  schedule: ["schedule"],
};
const GENERAL_FIELDS = ["transportOrderTypeId", "customer", "orderDate", "name", "customerReference", "lines", "references", "attributes"] as const;

const DRAFT = 1;
const OPEN = 2;
const READY_FOR_INVOICING = 3;
const INVOICED = 4;
const ARCHIVED = 5;

export interface NewOrderDefaults {
  transportOrderTypeId: string | null;
  orderDate: string;
}

export interface OrderEditorProps {
  mode: EditorMode;
  order?: TransportOrderDto;
  template?: TemplateDto;
  /** Without the Update permission the editor is read-only (UI-PERM-004). */
  readOnly: boolean;
  /** A new order (no `order`): the type and date it starts with. */
  newOrder?: NewOrderDefaults;
}

export function OrderEditor({ mode, order, template, readOnly, newOrder }: OrderEditorProps) {
  const { t } = useL();
  const closeRecord = useCloseRecord();
  const openRecord = useOpenRecord();
  const formRef = useRef<HTMLDivElement>(null);
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
  const [activeTab, setActiveTab] = useState<MainTab>("general");
  const [generalTab, setGeneralTab] = useState<GeneralTab>("items");
  /** The freight item open in Edit freight item, by form key. */
  const [editingKey, setEditingKey] = useState<string | null>(null);

  const isNew = mode === "order" && !order;
  const status = order?.status ?? DRAFT;
  const editableStatus = mode === "template" || status === DRAFT || status === OPEN;
  const formReadOnly = readOnly || !editableStatus;

  const initial = useMemo(() => {
    if (mode === "template") return templateValuesOf(template);
    if (order) return orderValuesOf(order);
    return { ...orderValuesOf(undefined), transportOrderTypeId: newOrder?.transportOrderTypeId ?? null, orderDate: newOrder?.orderDate ?? null };
  }, [mode, order, template, newOrder?.transportOrderTypeId, newOrder?.orderDate]);
  const validate = useCallback((v: OrderValues) => validateOrder(v, mode), [mode]);
  const { form, isDirty, submit, firstErrorField, reset } = useFormState<OrderValues>(initial, validate, formReadOnly);

  /** Takes the server's values (after a save or a reload); an item open in Edit freight item stays open (by position). */
  const adopt = (values: OrderValues, baseline?: OrderValues) => {
    const index = form.values.lines.findIndex((line) => line.key === editingKey);
    reset(values, baseline);
    if (editingKey) setEditingKey(index >= 0 ? (values.lines[index]?.key ?? null) : null);
  };

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
        adopt(initial);
        editBase.current = initial;
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loadedKey, initial]);

  const type = transportOrderTypes.useDetail(form.values.transportOrderTypeId ?? undefined);
  const storedId = mode === "order" ? order?.id : undefined;
  const rateLines = useOrderRateLines(storedId);
  useOrderRateLineUpdates(storedId);
  useTransportOrderUpdates(mode === "order" ? [order?.id] : []);
  /* Legs exist from Confirm on (design 58: Planning derives them from the stops). */
  const legsCreated = mode === "order" && order != null && status !== DRAFT;
  const legs = useLegsOfOrder(legsCreated ? order?.id : undefined);

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

  const create = useCreateTransportOrder();
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

  const isSaving = create.isPending || header.isPending || defaultStops.isPending || lines.isPending || references.isPending || terms.isPending || tripRequest.isPending || attributes.isPending || updateTemplate.isPending;
  const isBusy = isSaving || confirm.isPending || cancel.isPending || archive.isPending || remove.isPending || saveAsTemplate.isPending || removeTemplate.isPending;

  /** The newest version of the order (it also refreshes the page's query); null when it cannot be read. */
  const readFresh = (orderId: string) =>
    queryClient.fetchQuery({ queryKey: transportOrderKeys.detail(orderId), queryFn: () => fetchTransportOrder(orderId), staleTime: 0 }).catch(() => null);

  /** The trip request as the API takes it for the lines the server now holds: `lineIds` names the freight items on
   * the trip (null = all, design 61 "Order page, legacy tab structure" 8). */
  const tripRequestFor = (values: OrderValues, stored: TransportOrderDto): TransportOrderTripRequestInputDto | undefined => {
    const input = tripRequestInputOf(values.tripRequest);
    if (!input) return undefined;
    return { ...input, lineIds: tripLineIdsFor(values, stored.lines ?? []) };
  };

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
          return tripRequest.mutateAsync({ id: orderId, concurrencyStamp: stamp(), tripRequest: tripRequestFor(values, current) });
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
      adopt(mergeSavedSections(values, server, saved), server);
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
    adopt(orderValuesOf(current));
    editBase.current = orderValuesOf(current);
    if (steps.some((step) => step.command === "lines")) void queryClient.invalidateQueries({ queryKey: legKeys.ofOrder(orderId) });
    toast.success(t("Orders:TransportOrderSaved"));
    return true;
  };

  /**
   * Creates the new order in one call with everything typed (design 61 "Order page, legacy tab structure" 9), then
   * shows its page.
   * The new items carry client ids, so a Tevens trip for some of them names them (the server maps the ids to the
   * lines it creates); `confirmAfter` is Confirm or Ctrl/⌘+Enter on the new page.
   */
  const createOrder = async (values: OrderValues, confirmAfter: boolean): Promise<boolean> => {
    const posted = postedLineIdsOf(values.lines);
    const trip = tripRequestInputOf(values.tripRequest);
    let current: TransportOrderDto;
    try {
      current = await create.mutateAsync({
        transportOrderTypeId: values.transportOrderTypeId!,
        customerRelationId: values.customer!.id,
        customerContactPersonId: values.customerContactPersonId,
        orderDate: values.orderDate,
        customerReference: values.customerReference.trim() || null,
        organizationUnitId: values.organizationUnitId,
        internalRemarks: values.internalRemarks.trim() || null,
        invoicingRemarks: values.invoicingRemarks.trim() || null,
        attributes: values.attributes,
        lines: values.lines.map((line) => ({ ...lineInputOf(line), id: posted.get(line.key) })),
        references: referencesInputOf(values.references),
        financialTerms: termsInputOf(values.terms),
        tripRequest: trip ? { ...trip, lineIds: tripLineIdsPosted(values, posted) } : undefined,
        confirm: confirmAfter,
        source: 1,
      });
    } catch (err) {
      setServerError(getApiErrorMessage(err, t));
      return false;
    }
    const orderId = current.id!;
    /* The page now shows the stored order: the same editor, so the open tab and an open item stay. */
    queryClient.setQueryData(transportOrderKeys.detail(orderId), current);
    lastLoadedKey.current = `${current.id}|${current.concurrencyStamp ?? ""}`;
    adopt(orderValuesOf(current));
    editBase.current = orderValuesOf(current);
    toast.success(t(confirmAfter ? "Orders:TransportOrderConfirmed" : "Orders:TransportOrderSaved"));
    openRecord("TransportOrder", orderId, { replace: true, omitSearch: ["type"] });
    return true;
  };

  /** Reload after a conflict: the unsaved changes are discarded and the other user's version is loaded. */
  const reloadAfterConflict = async () => {
    if (!order?.id) return;
    const fresh = await fetchTransportOrder(order.id);
    queryClient.setQueryData(transportOrderKeys.detail(order.id), fresh);
    const values = orderValuesOf(fresh);
    lastLoadedKey.current = `${fresh.id}|${fresh.concurrencyStamp ?? ""}`;
    adopt(values);
    editBase.current = values;
    clearServerError();
    setConflict(null);
    toast.success(t("Orders:TransportOrderReloaded"));
  };

  /** Validation failed: open the tab (and General's nested tab) of the first error (UI-FORM-006). */
  const showErrors = () => {
    const field = firstErrorField();
    if (!field) return;
    const [main, nested] = TAB_OF_FIELD[field] ?? ["general"];
    setActiveTab(main);
    if (nested) setGeneralTab(nested);
  };

  /** Validates, then runs the changed sections' commands (or creates the new order); false when it did not save. */
  const save = async (confirmAfter = false): Promise<boolean> => {
    if (formReadOnly) return true;
    /* After a conflict nothing is saved until the order is reloaded (no automatic overwrite). */
    if (conflict) {
      setConflict("prompt");
      return false;
    }
    clearServerError();
    const values = submit(formRef.current);
    if (!values) {
      showErrors();
      return false;
    }
    if (isNew) return createOrder(values, confirmAfter);
    if (mode === "order" && order?.id) return saveOrder(order, values);
    try {
      if (mode === "template" && template?.id) {
        const posted = postedLineIdsOf(values.lines);
        const trip = templateTripRequestInputOf(values.tripRequest);
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
            /* No stop pattern on screen any more (PO 2026-10-26): what an older template stored is kept as it was. */
            defaultStops: values.defaultStops.map(defaultStopInputOf),
            lines: values.lines.map((line) => ({ ...templateLineInputOf(line), id: posted.get(line.key) })),
            references: referencesInputOf(values.references),
            financialTerms: termsInputOf(values.terms),
            tripRequest: trip ? { ...trip, lineIds: tripLineIdsPosted(values, posted) } : undefined,
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
        adopt(templateValuesOf(saved));
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

  const canConfirm = mode === "order" && status === DRAFT && can.confirm && !readOnly;
  const confirmOrder = () => {
    if (isNew) return void save(true);
    if (!order?.id) return;
    void runCommand(async () => {
      await confirm.mutateAsync(order.id!);
      void queryClient.invalidateQueries({ queryKey: legKeys.ofOrder(order.id) });
    }, "Orders:TransportOrderConfirmed");
  };

  const number = order?.number ?? "";
  const title =
    mode === "template"
      ? `${t("Orders:TransportOrderTemplate")} ${template?.name ?? ""}`
      : isNew
        ? t("Orders:NewTransportOrder")
        : `${t("Orders:TransportOrder")} ${number || t("Orders:TransportOrderStatus.Draft")}`;

  const commands: RibbonCommand[] = [];
  if (!formReadOnly) commands.push({ id: "save", labelKey: isSaving ? "Saving" : "Save", icon: Save, isPrimary: true, disabled: conflict != null, disabledReasonKey: "Orders:ReloadToSave", onClick: () => void save() });
  if (canConfirm) commands.push({ id: "confirm", labelKey: "Orders:Confirm", icon: CirclePlay, groupKey: "Orders:Status", onClick: confirmOrder });
  if (mode === "order" && order?.id) {
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
    /* A template without items would create empty orders (a new one starts without; its items are added here first). */
    const noLines = form.values.lines.length === 0;
    commands.push({ id: "materialise", labelKey: "Orders:Materialise", icon: CirclePlay, groupKey: "Orders:Documents", disabled: !template.isActive || noLines, disabledReasonKey: !template.isActive ? "OpenTms.Orders:TemplateInactive" : "Orders:TemplateHasNoLines", onClick: () => void saveIfDirty().then((ok) => ok && setDialog("materialise")) });
    if (can.deleteTemplate) commands.push({ id: "delete", labelKey: "Delete", icon: Trash2, variant: "destructive", groupKey: "Delete", onClick: () => setDialog("delete") });
  }

  /* The shortcuts the design names (UI-KEY-005), on the page only: Ctrl/⌘+S saves, Ctrl/⌘+Enter confirms a Draft. */
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const modifier = event.ctrlKey || event.metaKey;
    /* A key the page already handled (it reaches the page once more through the Edit freight item dialog's portal). */
    if (!modifier || event.altKey || event.shiftKey || event.defaultPrevented) return;
    if (event.key === "s" || event.key === "S") {
      event.preventDefault();
      if (!formReadOnly && !isBusy && conflict == null) void save();
    } else if (event.key === "Enter") {
      event.preventDefault();
      if (canConfirm && !isBusy) confirmOrder();
    }
  };

  const changed = useMemo(() => changedSections(form.values, initial), [form.values, initial]);
  const preview = usePricePreview(mode, storedId, changed.lines || changed.terms, form.values);
  const reportText = (report: { saved: SectionKey[]; notSaved: SectionKey[] }) =>
    `${report.saved.length > 0 ? `${t("Orders:SectionsSaved").replace("{0}", report.saved.map((s) => t(SECTION_LABEL_KEYS[s])).join(", "))} ` : ""}${t("Orders:SectionsNotSaved").replace("{0}", report.notSaved.map((s) => t(SECTION_LABEL_KEYS[s])).join(", "))}`;
  const printable = order ? [{ id: order.id!, number: order.number ?? "", lines: (order.lines ?? []).map((l, index) => ({ id: l.id!, position: l.position ?? index + 1, label: [l.transportUnitName, l.productName].filter(Boolean).join(" · ") })) }] : [];
  const storedLineIds = useMemo(() => form.values.lines.flatMap((line) => (line.id ? [line.id] : [])), [form.values.lines]);
  const attributeDefinitions = useOrderAttributeDefinitions(1);
  const hasCustomFields = (attributeDefinitions.data ?? []).length > 0 || Object.keys(form.values.attributes).length > 0;
  const hasError = (fields: readonly string[]) => fields.some((field) => form.error(field as keyof OrderValues & string) != null);

  /* The page's messages (a conflict, a refused save) show above the active tab's content (UI-FB-004: exactly once). */
  const withAlerts = (content: ReactNode, tab: MainTab) => (
    <div className="flex flex-col gap-4" data-slot={`order-tab-${tab}`}>
      {activeTab === tab && conflict && (
        <Alert variant="warning" data-slot="order-conflict">
          <TriangleAlert aria-hidden="true" />
          <AlertDescription>
            <p>{t("Orders:OrderChangedByOtherBanner")}</p>
            {saveReport && <p data-slot="save-report">{reportText(saveReport)}</p>}
            <div className="mt-2">
              <Button type="button" size="sm" variant="outline" onClick={() => setConflict("prompt")}>
                <RefreshCw /> {t("Orders:Reload")}
              </Button>
            </div>
          </AlertDescription>
        </Alert>
      )}
      {activeTab === tab && serverError && (
        <Alert variant="destructive">
          <AlertDescription>
            <p>{serverError}</p>
            {saveReport && <p data-slot="save-report">{reportText(saveReport)}</p>}
          </AlertDescription>
        </Alert>
      )}
      {content}
    </div>
  );

  const general = (
    <>
      <OrderHeader form={form} mode={mode} number={order?.number} type={type.data} stored={mode === "order" && !isNew} autoFocusCustomer={isNew} />
      <NestedTabs
        value={generalTab}
        onValueChange={(value) => setGeneralTab(value as GeneralTab)}
        tabs={[
          {
            value: "items",
            label: t("Orders:Lines"),
            hasError: hasError(["lines"]),
            content: (
              <FreightItems
                form={form}
                mode={mode}
                type={type.data}
                orderId={storedId}
                legsCreated={legsCreated}
                legs={legs.data}
                rateLines={rateLines.data}
                preview={preview}
                canEditRateLines={can.rateLines && editableStatus}
                editingKey={editingKey}
                onEdit={setEditingKey}
              />
            ),
          },
          {
            value: "services",
            label: t("Financial:AdditionalServices"),
            content: <AdditionalServices orderId={storedId} rateLines={rateLines.data} currency={form.values.terms.currencyCode} editable={can.rateLines && editableStatus && !formReadOnly} />,
          },
          { value: "plannerRemarks", label: t("Orders:PlannerRemarks"), content: <PlannerRemarks form={form} /> },
          { value: "references", label: t("Orders:OtherReferences"), hasError: hasError(["references"]), content: <OtherReferences form={form} /> },
          ...(hasCustomFields ? [{ value: "custom", label: t("Orders:CustomFields"), hasError: hasError(["attributes"]), content: <CustomFields form={form} definitions={attributeDefinitions.data} /> }] : []),
        ]}
      />
    </>
  );

  const tabs: EntityDialogTab[] = [
    { value: "general", label: t("General"), hasError: hasError(GENERAL_FIELDS), content: withAlerts(general, "general") },
    {
      value: "financial",
      label: t("Orders:Financial"),
      hasError: hasError(["terms"]),
      content: withAlerts(<FinancialTab form={form} mode={mode} order={order} rateLines={rateLines.data} canEditRateLines={can.rateLines && editableStatus} lineIds={storedLineIds} preview={preview} />, "financial"),
    },
    { value: "trip", label: t("Orders:TripRequestTab"), hasError: hasError(["tripRequest"]), content: withAlerts(<TripRequestSection form={form} mode={mode} order={order} />, "trip") },
    ...(mode === "template" ? [{ value: "schedule", label: t("Orders:Schedule"), hasError: hasError(["schedule"]), content: withAlerts(<ScheduleSection form={form} />, "schedule") }] : []),
    ...(mode === "order" && !isNew
      ? [
          {
            value: "documents",
            label: t("Orders:Documents"),
            icon: Paperclip,
            content: withAlerts(<DocumentsTab orderId={order?.id} onPrint={can.print && (order?.lines ?? []).length > 0 ? () => void saveIfDirty().then((ok) => ok && setDialog("print")) : undefined} />, "documents"),
          },
        ]
      : []),
    ...(mode === "order" ? [{ value: "remarks", label: t("Remarks"), icon: MessageSquareText, content: withAlerts(<OrderRemarks remarks={order?.remarks} />, "remarks") }] : []),
    (() => {
      const identification = recordIdentificationTab(t, { id: order?.id ?? template?.id, creationTime: order?.creationTime ?? template?.creationTime, lastModificationTime: order?.lastModificationTime ?? template?.lastModificationTime });
      return { ...identification, content: withAlerts(identification.content, "identification") };
    })(),
  ];

  return (
    <div ref={formRef} className="h-full" data-slot={mode === "order" ? "order-page" : "template-page"} data-record-id={order?.id ?? template?.id} data-status={orderStatusName(order?.status)} onKeyDown={onKeyDown}>
      <RecordPage
        title={title}
        badges={
          mode === "order" ? (
            <>
              {!isNew && <StatusBadge entity="TransportOrder" state={orderStatusName(order?.status)} />}
              <TripRequestWarningBadge request={order?.tripRequest} onOpen={() => setActiveTab("trip")} />
            </>
          ) : undefined
        }
        commands={commands}
        tabs={tabs}
        activeTab={activeTab}
        onActiveTabChange={(value) => setActiveTab(value as MainTab)}
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

