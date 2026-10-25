import { useEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Save, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { EntityDialog, type EntityDialogSize, type EntityDialogTab, type RibbonCommand } from "@/components/ui/entity-dialog";
import { masterDataQueryKey } from "@/domains/master-data/shared";
import { recordIdentificationTab } from "@/components/ui/record-identification-tab";
import { Skeleton } from "@/components/ui/skeleton";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The editor every Master Data entity shares (UI-DLG-002/004/005/008/009/010/015, UI-FORM-006/007):
 * one dialog per entity for create and edit, a Save ribbon, the dirty guard, validation on blur and
 * submit with the first erroneous tab opened, server errors as an Alert in the body, and the
 * Record identification tab last. The entity editor supplies the values, the fields and the DTOs. */

export type FieldErrors<TValues> = Partial<Record<keyof TValues & string, string>>;

export interface FormContext<TValues extends object> {
  values: TValues;
  set: <K extends keyof TValues & string>(field: K, value: TValues[K]) => void;
  /** A change computed from the latest values (a record created in place added to a list, UI-LOOK-004). */
  update: (change: (values: TValues) => TValues) => void;
  /** The error to show under a field: only after blur or submit (UI-FORM-006). */
  error: (field: keyof TValues & string) => string | undefined;
  blur: (field: keyof TValues & string) => void;
  readOnly: boolean;
  /** Prefix for control ids, unique per editor instance. */
  id: (field: string) => string;
}

export interface EditorTab<TValues extends object> {
  value: string;
  labelKey: string;
  /** The fields whose errors mark this tab (UI-FORM-007). */
  fields: (keyof TValues & string)[];
  render: (form: FormContext<TValues>) => ReactNode;
}

export interface MasterDataEditorProps<TValues extends object, TDto extends { id?: string; concurrencyStamp?: string | null; creationTime?: string; lastModificationTime?: string | null }> {
  entityKey: string;
  /** The resource of the entity's own texts: "MasterData" (default), the owning domain's ("Orders", "Financial"; designs 51–53) or "Platform" (design 54). */
  resource?: string;
  /** The query-key entity of the hooks (detail and lookup caches are invalidated after a save). */
  queryEntity: string;
  size: EntityDialogSize;
  mode: "create" | "edit";
  readOnly: boolean;
  open: boolean;
  onClose: () => void;
  onSaved: (saved?: unknown) => void;
  /** The loaded record in edit mode (TanStack query result shape). */
  detail: { data: TDto | undefined; isLoading: boolean; error: unknown };
  /** The record's display name for the title in edit mode. */
  titleOf: (dto: TDto) => string;
  initialValues: (dto: TDto | undefined) => TValues;
  /** Returns the localization keys of the field errors. */
  validate: (values: TValues) => FieldErrors<TValues>;
  /** Performs the create or update; receives the validated values. */
  save: (values: TValues, dto: TDto | undefined) => Promise<unknown>;
  isSaving: boolean;
  tabs: EditorTab<TValues>[];
  /** Extra ribbon commands after Save/Close (entity actions), e.g. Set as default. */
  extraCommands?: RibbonCommand[];
  /**
   * Ribbon commands that do not change this record and carry their own permission (a report definition's template
   * commands, design 50): after the extra commands, and shown in read-only mode too. A function receives whether the
   * form has unsaved changes (commands that act on the saved record wait for a save).
   */
  actionCommands?: RibbonCommand[] | ((state: { isDirty: boolean }) => RibbonCommand[]);
  /** One of `actionCommands` is running: the dialog is busy (UI-DLG-010). */
  isCommandBusy?: boolean;
  /** The failure of an `actionCommands` command, shown like a save's server error on the active tab (UI-FB-004). */
  commandError?: string | null;
  /** Delete from the ribbon (UI-ACT-002), confirmed, in edit mode; absent when the user may not delete. */
  deleteRecord?: { mutateAsync: (id: string) => Promise<unknown>; isPending: boolean };
  /** A business state that blocks Delete (UI-PERM-005): the reason's localization key, shown as the ribbon tooltip. */
  deleteDisabledReasonKey?: string;
  /** An extra body element above the tabs' content (warnings, UI-FB-008). */
  notice?: ReactNode;
  /** State badges after the title (UI-DLG-012). */
  badges?: ReactNode;
  /** An outside change to the values (a lookup's "Add new …" selecting the record it created): applied once per key. */
  applyValues?: { key: number; update: (values: TValues) => TValues };
  /** EntityDialog's fixed full height, for a tab that keeps part of itself fixed and scrolls a region (UI-DLG-006). */
  panelScroll?: boolean;
}

/** Empty strings become null, recursively, so dirty compares values and not touches (UI-FORM-009). */
export const normaliseValues = (value: unknown): unknown =>
  typeof value === "string"
    ? value.trim() || null
    : Array.isArray(value)
      ? value.map(normaliseValues)
      : value && typeof value === "object"
        ? Object.fromEntries(Object.entries(value).map(([k, v]) => [k, normaliseValues(v)]))
        : value;

let instanceCounter = 0;

export function MasterDataEditor<TValues extends object, TDto extends { id?: string; concurrencyStamp?: string | null; creationTime?: string; lastModificationTime?: string | null }>({
  entityKey,
  resource = "MasterData",
  queryEntity,
  size,
  mode,
  readOnly,
  open,
  onClose,
  onSaved,
  detail,
  titleOf,
  initialValues,
  validate,
  save,
  isSaving,
  tabs,
  extraCommands,
  actionCommands,
  isCommandBusy = false,
  commandError,
  deleteRecord,
  deleteDisabledReasonKey,
  notice,
  badges,
  applyValues,
  panelScroll,
}: MasterDataEditorProps<TValues, TDto>) {
  const { t } = useL();
  const queryClient = useQueryClient();
  const formRef = useRef<HTMLFormElement>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [instanceId] = useState(() => `md${++instanceCounter}`);

  const loaded = mode === "create" || detail.data != null;
  const initial = useMemo(() => initialValues(detail.data), [detail.data, initialValues]);
  const [values, setValues] = useState<TValues>(initial);
  const [touched, setTouched] = useState<Set<string>>(new Set());
  const [submitted, setSubmitted] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState(tabs[0]?.value ?? "general");

  /* The record arrives after the dialog opened: reset the form once, when it loads. */
  const loadedKey = detail.data?.id ?? "new";
  const lastLoadedKey = useRef<string | null>(null);
  useEffect(() => {
    if (loaded && lastLoadedKey.current !== loadedKey) {
      lastLoadedKey.current = loadedKey;
      setValues(initial);
    }
  }, [loaded, loadedKey, initial]);

  const lastAppliedKey = useRef<number | undefined>(undefined);
  useEffect(() => {
    if (applyValues && applyValues.key !== lastAppliedKey.current) {
      lastAppliedKey.current = applyValues.key;
      setValues(applyValues.update);
    }
  }, [applyValues]);

  const errors = useMemo(() => validate(values), [validate, values]);
  const isDirty = loaded && JSON.stringify(normaliseValues(values)) !== JSON.stringify(normaliseValues(initial));

  const form: FormContext<TValues> = {
    values,
    set: (field, value) => setValues((v) => ({ ...v, [field]: value })),
    update: (change) => setValues(change),
    error: (field) => (touched.has(field) || submitted ? (errors[field] ? t(errors[field]!) : undefined) : undefined),
    blur: (field) => setTouched((s) => (s.has(field) ? s : new Set(s).add(field))),
    readOnly,
    id: (field) => `${instanceId}-${field}`,
  };

  const handleSubmit = async (e?: FormEvent) => {
    e?.preventDefault();
    if (readOnly || isSaving || !loaded) return;
    setSubmitted(true);
    setServerError(null);

    const firstErrorField = Object.keys(errors)[0];
    if (firstErrorField) {
      const tab = tabs.find((tab) => tab.fields.includes(firstErrorField as keyof TValues & string));
      if (tab) setActiveTab(tab.value);
      requestAnimationFrame(() => formRef.current?.querySelector<HTMLElement>(`#${instanceId}-${firstErrorField}`)?.focus());
      return;
    }

    try {
      const saved = await save(values, detail.data);
      void queryClient.invalidateQueries({ queryKey: masterDataQueryKey(queryEntity) });
      onSaved(saved);
      onClose();
      toast.success(t(`${resource}:${entityKey}Saved`));
    } catch (err) {
      setServerError(getApiErrorMessage(err, t));
    }
  };

  const handleDelete = async () => {
    if (!deleteRecord || !detail.data?.id) return;
    try {
      await deleteRecord.mutateAsync(detail.data.id);
      void queryClient.invalidateQueries({ queryKey: masterDataQueryKey(queryEntity) });
      setConfirmDelete(false);
      onSaved();
      onClose();
      toast.success(t(`${resource}:${entityKey}Deleted`));
    } catch (err) {
      setConfirmDelete(false);
      setServerError(getApiErrorMessage(err, t));
    }
  };

  const title = mode === "create" ? t(`${resource}:New${entityKey}`) : `${t(`${resource}:${entityKey}`)} ${detail.data ? titleOf(detail.data) : ""}`;

  const recordCommands = typeof actionCommands === "function" ? actionCommands({ isDirty }) : (actionCommands ?? []);
  const commands: RibbonCommand[] = readOnly
    ? recordCommands
    : [
        {
          id: "save",
          labelKey: isSaving ? "Saving" : "Save",
          icon: Save,
          isPrimary: true,
          disabled: !loaded,
          onClick: () => formRef.current?.requestSubmit(),
        },
        ...(extraCommands ?? []),
        ...recordCommands,
        ...(mode === "edit" && deleteRecord
          ? [{ id: "delete", labelKey: "Delete", icon: Trash2, variant: "destructive" as const, groupKey: "Delete", disabled: !loaded || deleteDisabledReasonKey != null, disabledReasonKey: deleteDisabledReasonKey, onClick: () => setConfirmDelete(true) }]
          : []),
      ];

  /* A server error shows on the tab the user is on (a save from the third tab must not hide it on the first); on the
   * record-identification tab, which has no form, it shows on the first tab. */
  const errorTab = tabs.some((tab) => tab.value === activeTab) ? activeTab : tabs[0]?.value;

  const body = (render: (form: FormContext<TValues>) => ReactNode, first: boolean, showsServerError: boolean) =>
    !loaded ? (
      <div className="grid grid-cols-1 gap-3">
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-10 w-2/3" />
      </div>
    ) : (
      /* h-full: the panel's height reaches the tab's content, so a tab can fill it and scroll a region of itself
       * (UI-DLG-006); the alerts and the notice keep their own height, the content below them shrinks. */
      <div className="flex h-full min-h-0 flex-col gap-4">
        {showsServerError && serverError && (
          <Alert variant="destructive">
            <AlertDescription>{serverError}</AlertDescription>
          </Alert>
        )}
        {showsServerError && commandError && (
          <Alert variant="destructive">
            <AlertDescription>{commandError}</AlertDescription>
          </Alert>
        )}
        {first && detail.error != null && (
          <Alert variant="destructive">
            <AlertDescription>{getApiErrorMessage(detail.error, t)}</AlertDescription>
          </Alert>
        )}
        {first && notice}
        {render(form)}
      </div>
    );

  const dialogTabs: EntityDialogTab[] = [
    ...tabs.map((tab, index) => ({
      value: tab.value,
      label: t(tab.labelKey),
      hasError: tab.fields.some((field) => form.error(field) != null),
      content:
        index === 0 ? (
          <form ref={formRef} onSubmit={(e) => void handleSubmit(e)} noValidate className="h-full min-h-0">
            {body(tab.render, true, tab.value === errorTab)}
            <button type="submit" className="hidden" aria-hidden="true" tabIndex={-1} />
          </form>
        ) : (
          body(tab.render, false, tab.value === errorTab)
        ),
    })),
    recordIdentificationTab(t, {
      id: mode === "edit" ? detail.data?.id : undefined,
      creationTime: detail.data?.creationTime,
      lastModificationTime: detail.data?.lastModificationTime,
    }),
  ];

  return (
    <>
      <EntityDialog
        open={open}
        onOpenChange={(next) => {
          if (!next) onClose();
        }}
        variant="editor"
        size={size}
        title={title}
        badges={badges}
        commands={commands}
        tabs={dialogTabs}
        activeTab={activeTab}
        onActiveTabChange={setActiveTab}
        isDirty={isDirty}
        isBusy={isSaving || deleteRecord?.isPending === true || isCommandBusy}
        panelScroll={panelScroll}
      />
      {deleteRecord && (
        <ConfirmDialog
          open={confirmDelete}
          onOpenChange={(next) => {
            if (!next) setConfirmDelete(false);
          }}
          title={t(`${resource}:Delete${entityKey}`)}
          description={`${t(`${resource}:Delete${entityKey}Question`).replace("{0}", detail.data ? titleOf(detail.data) : "")} ${t("ThisCannotBeUndone")}`}
          confirmLabel={t("Delete")}
          busyLabel={t("Deleting")}
          destructive
          onConfirm={handleDelete}
        />
      )}
    </>
  );
}
