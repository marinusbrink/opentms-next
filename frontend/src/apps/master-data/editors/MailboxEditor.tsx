import { useCallback, useMemo, useState } from "react";
import { Pause, Play, RefreshCw } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { StatusBadge } from "@/components/ui/status-badge";
import type { EntityEditorProps } from "@/components/record-editor/editor-props";
import { CheckboxField, TextField } from "@/components/record-editor/fields";
import { MasterDataEditor, type FieldErrors, type FormContext } from "@/components/record-editor/MasterDataEditor";
import type { RibbonCommand } from "@/components/ui/entity-dialog";
import { RelationField, relationOptionOf } from "@/domains/master-data/components/RelationFields";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import { useLookupOptions } from "@/domains/master-data/shared";
import type { RelationOption } from "@/domains/master-data/entities";
import {
  MAILBOX_STATUSES,
  MAIL_PROVIDERS,
  enumLabel,
  enumName,
  mailboxErrorText,
  useDeleteMailbox,
  useMailbox,
  useMailboxFolders,
  usePauseMailbox,
  useReconnectMailbox,
  useResumeMailbox,
  useUpdateMailbox,
  type MailboxDto,
} from "@/domains/integrations/order-intake";
import { transportOrderTypes } from "@/domains/orders/reference-data";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* A mailbox connection (design 72 "UI design", Mailboxes): name, the label or folder to read (from the provider's list),
 * the default customer and order type (pickers with "Add new …", UI-LOOK-004), auto-confirm with its explanation, the
 * status in the header (UI-DLG-012). A connection is only made through Connect (the provider's consent), so this
 * editor edits; Reconnect, Pause and Resume are ribbon commands, Delete is last. Read-only without
 * Integrations.Mailboxes.Manage (UI-PERM-004). Opened from the OAuth return (?connected=) it says to choose the folder. */

interface MailboxValues {
  displayName: string;
  folderId: string | null;
  defaultCustomer: RelationOption | null;
  defaultTransportOrderTypeId: string | null;
  autoConfirm: boolean;
}

const validate = (v: MailboxValues): FieldErrors<MailboxValues> => (v.displayName.trim() ? {} : { displayName: "MasterData:Required" });

/** The relation roles of a customer (RELATION_ROLES: Customer = 1); a new relation from the picker is a customer. */
const CUSTOMER_ROLE = 1;

export interface MailboxEditorProps extends Omit<EntityEditorProps, "mode" | "initialName"> {
  id: string;
}

export function MailboxEditor({ id, readOnly, open, onClose, onSaved }: MailboxEditorProps) {
  const { t } = useL();
  const f = useFormat();
  const canManage = useIsGranted("Integrations.Mailboxes.Manage");
  const detail = useMailbox(id);
  const update = useUpdateMailbox();
  const remove = useDeleteMailbox();
  const reconnect = useReconnectMailbox();
  const pause = usePauseMailbox();
  const resume = useResumeMailbox();
  const [commandError, setCommandError] = useState<string | null>(null);
  const inPlace = useCreateInPlace();
  const typeOptions = useLookupOptions(transportOrderTypes.useLookup(open));

  const status = enumName(MAILBOX_STATUSES, detail.data?.status);
  const consented = status === "AwaitingFolder" || status === "Active" || status === "Paused";
  const folders = useMailboxFolders(id, open && canManage && !readOnly && consented);
  const folderOptions = useMemo(() => {
    const list = (folders.data ?? []).map((folder) => ({ value: folder.id ?? "", label: folder.name ?? folder.id ?? "" }));
    /* The stored folder stays selectable when the provider's list could not be read. */
    const stored = detail.data?.folderId;
    return stored && !list.some((o) => o.value === stored) ? [{ value: stored, label: detail.data?.folderName ?? stored }, ...list] : list;
  }, [folders.data, detail.data?.folderId, detail.data?.folderName]);

  const initialValues = useCallback(
    (dto: MailboxDto | undefined): MailboxValues => ({
      displayName: dto?.displayName ?? "",
      folderId: dto?.folderId ?? null,
      defaultCustomer: relationOptionOf(dto?.defaultCustomerRelationId, dto?.defaultCustomerName),
      defaultTransportOrderTypeId: dto?.defaultTransportOrderTypeId ?? null,
      autoConfirm: dto?.autoConfirm ?? false,
    }),
    [],
  );

  const save = async (values: MailboxValues, dto: MailboxDto | undefined) =>
    update.mutateAsync({
      id,
      dto: {
        displayName: values.displayName.trim(),
        folderId: values.folderId,
        defaultCustomerRelationId: values.defaultCustomer?.id ?? null,
        defaultTransportOrderTypeId: values.defaultTransportOrderTypeId,
        autoConfirm: values.autoConfirm,
        concurrencyStamp: dto?.concurrencyStamp ?? "",
      },
    });

  const run = async (action: () => Promise<unknown>, successKey: string) => {
    setCommandError(null);
    try {
      await action();
      toast.success(t(successKey));
      onSaved();
      void detail.refetch();
    } catch (err) {
      setCommandError(getApiErrorMessage(err, t));
    }
  };

  const actionCommands: RibbonCommand[] = canManage
    ? [
        ...(status === "AwaitingConsent" || status === "Reconnect"
          ? [
              {
                id: "reconnect",
                labelKey: "Integrations:Reconnect",
                icon: RefreshCw,
                groupKey: "Integrations:Connection",
                onClick: () => {
                  setCommandError(null);
                  reconnect.mutateAsync(id).then(
                    (result) => result?.authorizeUrl && window.location.assign(result.authorizeUrl),
                    (err: unknown) => setCommandError(getApiErrorMessage(err, t)),
                  );
                },
              },
            ]
          : []),
        ...(status === "Active" ? [{ id: "pause", labelKey: "Integrations:Pause", icon: Pause, groupKey: "Integrations:Connection", onClick: () => void run(() => pause.mutateAsync(id), "Integrations:MailboxPaused") }] : []),
        ...(status === "Paused" ? [{ id: "resume", labelKey: "Integrations:Resume", icon: Play, groupKey: "Integrations:Connection", onClick: () => void run(() => resume.mutateAsync(id), "Integrations:MailboxResumed") }] : []),
      ]
    : [];

  const dto = detail.data;
  const renderGeneral = (form: FormContext<MailboxValues>) => (
    <div className="grid grid-cols-1 gap-3">
      <TextField form={form} field="displayName" labelKey="Integrations:MailboxName" required maxLength={128} />
      {canManage && !form.readOnly && consented ? (
        <FormField
          label={t("Integrations:Folder")}
          htmlFor={form.id("folderId")}
          floating="always"
          required={status === "AwaitingFolder"}
          error={folders.error ? getApiErrorMessage(folders.error, t) : undefined}
          hint={t("Integrations:Folder.Hint")}
        >
          {/* DEVIATION(UI-LOOK-004): the provider's labels or folders are no OpenTMS records and cannot be created from here (the
            * mailbox's owner makes them at Google or Microsoft), approved in docs/designs/72-order-intake.md, "UI design": "folder
            * (select from the provider's list)". */}
          <ReferenceCombobox id={form.id("folderId")} value={form.values.folderId} onChange={(next) => form.set("folderId", next)} options={folderOptions} required={status === "AwaitingFolder"} />
        </FormField>
      ) : (
        <FormField label={t("Integrations:Folder")} htmlFor={form.id("folderName")}>
          <Input id={form.id("folderName")} value={dto?.folderName ?? ""} readOnly disabled={form.readOnly} />
        </FormField>
      )}
      <RelationField
        id={form.id("defaultCustomer")}
        label={t("Integrations:DefaultCustomer")}
        value={form.values.defaultCustomer}
        onChange={(next) => form.set("defaultCustomer", next)}
        roles={CUSTOMER_ROLE}
        disabled={form.readOnly}
      />
      <FormField label={t("Integrations:DefaultTransportOrderType")} htmlFor={form.id("defaultTransportOrderTypeId")} floating="always">
        <ReferenceCombobox
          id={form.id("defaultTransportOrderTypeId")}
          value={form.values.defaultTransportOrderTypeId}
          onChange={(next) => form.set("defaultTransportOrderTypeId", next)}
          options={typeOptions}
          disabled={form.readOnly}
          {...inPlace.offer("TransportOrderType", form.readOnly, (r) => form.set("defaultTransportOrderTypeId", r.id))}
        />
      </FormField>
      <div className="grid gap-1">
        <CheckboxField form={form} field="autoConfirm" labelKey="Integrations:AutoConfirm" describedBy={`${form.id("autoConfirm")}-explanation`} />
        <p id={`${form.id("autoConfirm")}-explanation`} className="text-label text-muted-foreground">
          {t("Integrations:AutoConfirm.Explanation")}
        </p>
      </div>
      {dto && (
        <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 rounded-lg border border-border-section bg-surface-section p-4 text-sm">
          <dt className="text-label font-medium text-muted-foreground">{t("Integrations:Provider")}</dt>
          <dd>{enumLabel(t, "MailProvider", MAIL_PROVIDERS, dto.provider)}</dd>
          <dt className="text-label font-medium text-muted-foreground">{t("Integrations:EmailAddress")}</dt>
          <dd className="break-all">{dto.emailAddress ?? ""}</dd>
          <dt className="text-label font-medium text-muted-foreground">{t("Integrations:ConnectedAt")}</dt>
          <dd className="tabular-nums">{dto.connectedAt ? f.dateTime(dto.connectedAt) : ""}</dd>
          <dt className="text-label font-medium text-muted-foreground">{t("Integrations:LastSyncAt")}</dt>
          <dd className="tabular-nums">{dto.lastSyncAt ? f.dateTime(dto.lastSyncAt) : ""}</dd>
          {dto.lastErrorCode && (
            <>
              <dt className="text-label font-medium text-muted-foreground">{t("Integrations:LastError")}</dt>
              <dd>
                {mailboxErrorText(t, dto.lastErrorCode)}
                {dto.lastErrorAt && <span className="block text-label text-muted-foreground tabular-nums">{f.dateTime(dto.lastErrorAt)}</span>}
              </dd>
            </>
          )}
        </dl>
      )}
      {inPlace.dialog}
    </div>
  );

  return (
    <MasterDataEditor<MailboxValues, MailboxDto>
      entityKey="Mailbox"
      resource="Integrations"
      queryEntity="integrations.mailboxes"
      size="sm"
      mode="edit"
      readOnly={readOnly}
      open={open}
      onClose={onClose}
      onSaved={onSaved}
      detail={detail}
      titleOf={(d) => d.displayName ?? ""}
      initialValues={initialValues}
      validate={validate}
      save={save}
      isSaving={update.isPending}
      badges={status ? <StatusBadge entity="MailboxConnection" state={status} /> : undefined}
      notice={
        status === "AwaitingFolder" ? (
          <Alert variant="warning">
            <AlertDescription>{t("Integrations:ChooseFolderNotice")}</AlertDescription>
          </Alert>
        ) : status === "Reconnect" || status === "AwaitingConsent" ? (
          <Alert variant="warning">
            <AlertDescription>{t(status === "Reconnect" ? "Integrations:ReconnectNotice" : "Integrations:AwaitingConsentNotice")}</AlertDescription>
          </Alert>
        ) : undefined
      }
      actionCommands={actionCommands}
      isCommandBusy={reconnect.isPending || pause.isPending || resume.isPending}
      commandError={commandError}
      deleteRecord={canManage ? remove : undefined}
      tabs={[{ value: "general", labelKey: "General", fields: ["displayName", "folderId"], render: renderGeneral }]}
    />
  );
}
