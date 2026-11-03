import { useCallback, useState } from "react";
import { KeyRound, Power, PowerOff } from "lucide-react";
import { toast } from "sonner";

import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { CopyButton } from "@/components/ui/copy-button";
import type { RibbonCommand } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { StatusBadge } from "@/components/ui/status-badge";
import type { EntityEditorProps } from "@/components/record-editor/editor-props";
import { CheckboxField, TextField } from "@/components/record-editor/fields";
import { MasterDataEditor, type FieldErrors, type FormContext } from "@/components/record-editor/MasterDataEditor";
import { RelationField, relationOptionOf } from "@/domains/master-data/components/RelationFields";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import type { RelationOption } from "@/domains/master-data/entities";
import { useLookupOptions } from "@/domains/master-data/shared";
import {
  useActivateApiClient,
  useApiClient,
  useCreateApiClient,
  useDeactivateApiClient,
  useDeleteApiClient,
  useRegenerateApiClientSecret,
  useUpdateApiClient,
  type ApiClientDto,
  type ApiClientSecret,
} from "@/domains/integrations/order-intake";
import { transportOrderTypes } from "@/domains/orders/reference-data";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* An API client (design 72 "Channels", "UI design"): the name, the customer whose orders it sends (a relation picker with
 * "Add new …", UI-LOOK-004), the default order type and auto-confirm; in edit mode also the client id (copy) and when its
 * secret was made. Create and Regenerate secret hand the one-time secret to the list (`onSecret`), which shows it once.
 * Activate / Deactivate are ribbon commands, Delete is last. Read-only without Integrations.ApiClients.Manage. */

interface ApiClientValues {
  name: string;
  customer: RelationOption | null;
  defaultTransportOrderTypeId: string | null;
  autoConfirm: boolean;
}

const validate = (v: ApiClientValues): FieldErrors<ApiClientValues> => (v.name.trim() ? {} : { name: "MasterData:Required" });

const CUSTOMER_ROLE = 1;

export interface ApiClientEditorProps extends Omit<EntityEditorProps, "initialName"> {
  /** The one-time secret of a create or a regenerate, with the client's name. */
  onSecret: (secret: ApiClientSecret, name: string) => void;
}

export function ApiClientEditor({ mode, id, readOnly, open, onClose, onSaved, onSecret }: ApiClientEditorProps) {
  const { t } = useL();
  const f = useFormat();
  const canManage = useIsGranted("Integrations.ApiClients.Manage");
  const detail = useApiClient(mode === "edit" ? id : undefined);
  const create = useCreateApiClient();
  const update = useUpdateApiClient();
  const remove = useDeleteApiClient();
  const regenerate = useRegenerateApiClientSecret();
  const activate = useActivateApiClient();
  const deactivate = useDeactivateApiClient();
  const [commandError, setCommandError] = useState<string | null>(null);
  const [confirmRegenerate, setConfirmRegenerate] = useState(false);
  const inPlace = useCreateInPlace();
  const typeOptions = useLookupOptions(transportOrderTypes.useLookup(open));

  const initialValues = useCallback(
    (dto: ApiClientDto | undefined): ApiClientValues => ({
      name: dto?.name ?? "",
      customer: relationOptionOf(dto?.customerRelationId, dto?.customerName),
      defaultTransportOrderTypeId: dto?.defaultTransportOrderTypeId ?? null,
      autoConfirm: dto?.autoConfirm ?? false,
    }),
    [],
  );

  const save = async (values: ApiClientValues, dto: ApiClientDto | undefined) => {
    const body = {
      name: values.name.trim(),
      customerRelationId: values.customer?.id ?? null,
      defaultTransportOrderTypeId: values.defaultTransportOrderTypeId,
      autoConfirm: values.autoConfirm,
    };
    if (mode === "create") {
      const secret = await create.mutateAsync(body);
      onSecret(secret, body.name);
      return secret;
    }
    return update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
  };

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

  const dto = detail.data;
  const actionCommands: RibbonCommand[] =
    canManage && mode === "edit" && dto
      ? [
          { id: "regenerate", labelKey: "Integrations:RegenerateSecret", icon: KeyRound, groupKey: "Integrations:Access", onClick: () => setConfirmRegenerate(true) },
          dto.isActive
            ? { id: "deactivate", labelKey: "Integrations:Deactivate", icon: PowerOff, groupKey: "Integrations:Access", onClick: () => void run(() => deactivate.mutateAsync(id!), "Integrations:ApiClientDeactivated") }
            : { id: "activate", labelKey: "Integrations:Activate", icon: Power, groupKey: "Integrations:Access", onClick: () => void run(() => activate.mutateAsync(id!), "Integrations:ApiClientActivated") },
        ]
      : [];

  const renderGeneral = (form: FormContext<ApiClientValues>) => (
    <div className="grid grid-cols-1 gap-3">
      <TextField form={form} field="name" labelKey="Integrations:ApiClientName" required maxLength={128} />
      <RelationField
        id={form.id("customer")}
        label={t("Integrations:Customer")}
        value={form.values.customer}
        onChange={(next) => form.set("customer", next)}
        roles={CUSTOMER_ROLE}
        disabled={form.readOnly}
      />
      <p className="-mt-2 text-label text-muted-foreground">{t("Integrations:ApiClientCustomer.Hint")}</p>
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
          {t("Integrations:AutoConfirm.ApiExplanation")}
        </p>
      </div>
      {mode === "edit" && dto && (
        <dl className="grid grid-cols-[auto_1fr] items-center gap-x-6 gap-y-2 rounded-lg border border-border-section bg-surface-section p-4 text-sm">
          <dt className="text-label font-medium text-muted-foreground">{t("Integrations:ClientId")}</dt>
          <dd className="flex items-center gap-2">
            <code className="rounded-md bg-background px-2 py-1 font-mono text-label select-all">{dto.clientId}</code>
            {dto.clientId && <CopyButton value={dto.clientId} label={t("Integrations:CopyValue").replace("{0}", t("Integrations:ClientId"))} />}
          </dd>
          <dt className="text-label font-medium text-muted-foreground">{t("Integrations:ClientSecret")}</dt>
          <dd className="tabular-nums">
            {t("Integrations:SecretEndingIn").replace("{0}", dto.secretLastFour ?? "").replace("{1}", dto.secretCreatedAt ? f.dateTime(dto.secretCreatedAt) : "")}
          </dd>
          <dt className="text-label font-medium text-muted-foreground">{t("Integrations:LastUsedAt")}</dt>
          <dd className="tabular-nums">{dto.lastUsedAt ? f.dateTime(dto.lastUsedAt) : t("Integrations:NeverUsed")}</dd>
        </dl>
      )}
      {inPlace.dialog}
    </div>
  );

  return (
    <>
      <MasterDataEditor<ApiClientValues, ApiClientDto>
        entityKey="ApiClient"
        resource="Integrations"
        queryEntity="integrations.apiClients"
        size="sm"
        mode={mode}
        readOnly={readOnly}
        open={open}
        onClose={onClose}
        onSaved={onSaved}
        detail={mode === "edit" ? detail : { data: undefined, isLoading: false, error: null }}
        titleOf={(d) => d.name ?? ""}
        initialValues={initialValues}
        validate={validate}
        save={save}
        isSaving={create.isPending || update.isPending}
        badges={mode === "edit" && dto ? <StatusBadge entity="ApiClient" state={dto.isActive ? "Active" : "Inactive"} /> : undefined}
        actionCommands={actionCommands}
        isCommandBusy={regenerate.isPending || activate.isPending || deactivate.isPending}
        commandError={commandError}
        deleteRecord={canManage ? remove : undefined}
        tabs={[{ value: "general", labelKey: "General", fields: ["name"], render: renderGeneral }]}
      />
      <ConfirmDialog
        open={confirmRegenerate}
        onOpenChange={setConfirmRegenerate}
        title={t("Integrations:RegenerateSecret")}
        description={t("Integrations:RegenerateSecretQuestion").replace("{0}", dto?.name ?? "")}
        confirmLabel={t("Integrations:Regenerate")}
        busyLabel={t("Integrations:Regenerating")}
        destructive
        onConfirm={async () => {
          const secret = await regenerate.mutateAsync(id!);
          setConfirmRegenerate(false);
          onSecret(secret, dto?.name ?? "");
          void detail.refetch();
        }}
      />
    </>
  );
}
