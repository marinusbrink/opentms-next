import { useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Save } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import type { AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { EntityDialog, type EntityDialogTab } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { recordIdentificationTab } from "@/components/ui/record-identification-tab";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { StatusBadge } from "@/components/ui/status-badge";
import { Textarea } from "@/components/ui/textarea";
import {
  TenantProvisioningStates,
  type EditionLookup,
  useCreateTenant,
  useEditionLookup,
  useTenant,
  useTenantConnectionPreview,
  useUpdateTenant,
  type TenantRow,
} from "@/domains/platform/tenants";
import { EditionFormDialog } from "@/apps/tenants/EditionFormDialog";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface TenantFormDialogProps {
  mode: "create" | "edit";
  tenant?: TenantRow;
  readOnly?: boolean;
  open: boolean;
  onClose: () => void;
  onSuccess: () => void;
  /** Platform.Editions.Create (the view checks it, as for the edition editor's canManageFeatures): the edition
   *  picker offers "Add new edition…" (UI-LOOK-004). */
  canCreateEdition?: boolean;
}

interface TenantFormValues {
  name: string;
  editionId: string | null;
  adminEmailAddress: string;
}

/** Same shape check as the backend's [EmailAddress]: something@something. */
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+$/;
const PREVIEW_DEBOUNCE_MS = 300;

/* The tenant editor (docs/designs/tenants-and-editions.md). What the old app asked for and the PO took off
 * (D1, D2, D6): no "use host database" (every tenant gets its own), no password (the administrator is invited),
 * no change-password / activation / active switches (always so). The connection string is generated: shown
 * read-only with the password masked. */
export function TenantFormDialog({ mode, tenant, readOnly = false, open, onClose, onSuccess, canCreateEdition = false }: TenantFormDialogProps) {
  const { t } = useL();
  const createTenant = useCreateTenant();
  const updateTenant = useUpdateTenant();
  const editions = useEditionLookup();
  const detail = useTenant(mode === "edit" ? tenant?.id : undefined);
  const formRef = useRef<HTMLFormElement>(null);
  /* "Add new …" in the edition picker (UI-LOOK-004): the edition editor on top; the saved edition is selected. */
  const [newEdition, setNewEdition] = useState<{ term: string } | null>(null);

  const initial = useMemo<TenantFormValues>(
    () => ({
      name: tenant?.name ?? "",
      editionId: tenant?.editionId ?? null,
      adminEmailAddress: "",
    }),
    [tenant],
  );

  const [values, setValues] = useState<TenantFormValues>(initial);
  const [touched, setTouched] = useState<Record<keyof TenantFormValues, boolean>>({
    name: false,
    editionId: false,
    adminEmailAddress: false,
  });
  const [submitted, setSubmitted] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState("general");

  /* The preview follows the typed name, debounced. */
  const [previewName, setPreviewName] = useState(values.name.trim());
  useEffect(() => {
    const timer = window.setTimeout(() => setPreviewName(values.name.trim()), PREVIEW_DEBOUNCE_MS);
    return () => window.clearTimeout(timer);
  }, [values.name]);
  const preview = useTenantConnectionPreview(previewName, mode === "create" && open);

  const isPending = createTenant.isPending || updateTenant.isPending;
  const isDirty = JSON.stringify(values) !== JSON.stringify(initial);

  const errors: Partial<Record<keyof TenantFormValues, string>> = {};
  if (!values.name.trim()) errors.name = t("FieldRequired");
  if (mode === "create") {
    if (!values.adminEmailAddress.trim()) errors.adminEmailAddress = t("FieldRequired");
    else if (!EMAIL_PATTERN.test(values.adminEmailAddress.trim())) errors.adminEmailAddress = t("Tenants:InvalidEmail");
  }
  const shown = (field: keyof TenantFormValues) => (touched[field] || submitted ? errors[field] : undefined);

  const handleSubmit = async (e?: FormEvent) => {
    e?.preventDefault();
    if (readOnly || isPending) return;
    setSubmitted(true);
    setServerError(null);
    const firstInvalid = (["name", "adminEmailAddress"] as const).find((field) => errors[field]);
    if (firstInvalid) {
      setActiveTab("general");
      requestAnimationFrame(() => formRef.current?.querySelector<HTMLElement>(`#tf-${firstInvalid}`)?.focus());
      return;
    }

    try {
      if (mode === "create") {
        await createTenant.mutateAsync({
          name: values.name.trim(),
          adminEmailAddress: values.adminEmailAddress.trim(),
          editionId: values.editionId,
        });
        toast.success(t("Tenants:TenantCreated"));
      } else {
        await updateTenant.mutateAsync({
          id: tenant!.id!,
          dto: { name: values.name.trim(), editionId: values.editionId },
        });
        toast.success(t("Tenants:TenantSaved"));
      }
      onSuccess();
      onClose();
    } catch (err) {
      setServerError(getApiErrorMessage(err, t));
    }
  };

  const title = mode === "create" ? t("Tenants:NewTenant") : `${t("Tenants:Tenant")} ${tenant?.name ?? ""}`;

  const commands: AppCommandBarCommand[] = readOnly
    ? []
    : [
        {
          id: "save",
          labelKey: isPending ? "Saving" : "Save",
          icon: Save,
          isPrimary: true,
          onClick: () => formRef.current?.requestSubmit(),
        },
      ];

  const editionOptions = (editions.data ?? []).map((edition) => ({
    value: edition.id!,
    label: edition.displayName ?? "",
  }));

  const connectionString =
    mode === "create" ? (values.name.trim() ? (preview.data?.connectionString ?? "") : "") : (detail.data?.connectionString ?? "");
  const provisioningError =
    detail.data?.provisioningState === TenantProvisioningStates.Failed ? detail.data.provisioningError : null;

  const generalTab: EntityDialogTab = {
    value: "general",
    label: t("General"),
    hasError: shown("name") != null || shown("adminEmailAddress") != null,
    content: (
      <form ref={formRef} onSubmit={(e) => void handleSubmit(e)} className="grid grid-cols-1 gap-4" noValidate>
        {serverError && (
          <Alert variant="destructive">
            <AlertDescription>{serverError}</AlertDescription>
          </Alert>
        )}
        {provisioningError && (
          <Alert variant="destructive">
            <AlertDescription>
              {t("Tenants:ProvisioningFailedDetail").replace("{0}", provisioningError)}
            </AlertDescription>
          </Alert>
        )}

        <FormSection title={t("Tenants:Tenant")}>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <FormField label={t("Tenants:Name")} htmlFor="tf-name" required error={shown("name")}>
              <Input
                id="tf-name"
                value={values.name}
                onChange={(e) => setValues((v) => ({ ...v, name: e.target.value }))}
                onBlur={() => setTouched((s) => ({ ...s, name: true }))}
                maxLength={64}
                disabled={readOnly}
              />
            </FormField>
            <FormField label={t("Tenants:Edition")} htmlFor="tf-edition">
              <ReferenceCombobox
                id="tf-edition"
                value={values.editionId}
                onChange={(next) => setValues((v) => ({ ...v, editionId: next }))}
                options={editionOptions}
                disabled={readOnly}
                onCreateNew={canCreateEdition && !readOnly ? (term) => setNewEdition({ term }) : undefined}
                createLabel={t("Editions:AddNewEdition")}
              />
            </FormField>
          </div>
        </FormSection>

        {mode === "create" && (
          <FormSection title={t("Tenants:Administrator")}>
            <FormField
              label={t("Tenants:AdminEmailAddress")}
              htmlFor="tf-adminEmailAddress"
              required
              error={shown("adminEmailAddress")}
              hint={t("Tenants:AdminEmailHint")}
            >
              <Input
                id="tf-adminEmailAddress"
                type="email"
                autoComplete="off"
                value={values.adminEmailAddress}
                onChange={(e) => setValues((v) => ({ ...v, adminEmailAddress: e.target.value }))}
                onBlur={() => setTouched((s) => ({ ...s, adminEmailAddress: true }))}
                maxLength={256}
              />
            </FormField>
          </FormSection>
        )}

        <FormSection title={t("Tenants:Database")}>
          <FormField
            label={t("Tenants:ConnectionString")}
            htmlFor="tf-connection"
            floating="always"
            hint={mode === "create" ? t("Tenants:ConnectionStringHint") : undefined}
          >
            <Textarea
              id="tf-connection"
              value={connectionString}
              readOnly
              rows={3}
              className="font-mono text-label"
            />
          </FormField>
        </FormSection>

        <button type="submit" className="hidden" aria-hidden="true" tabIndex={-1} />
      </form>
    ),
  };

  const tabs: EntityDialogTab[] = [
    generalTab,
    recordIdentificationTab(t, {
      id: mode === "edit" ? tenant?.id : undefined,
      creationTime: mode === "edit" ? tenant?.creationTime : undefined,
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
      size="md"
      title={title}
      badges={
        mode === "edit" ? <StatusBadge entity="TenantProvisioning" state={detail.data?.provisioningState ?? tenant?.provisioningState} /> : undefined
      }
      commands={commands}
      tabs={tabs}
      activeTab={activeTab}
      onActiveTabChange={setActiveTab}
      isDirty={isDirty}
      isBusy={isPending}
    />
    {newEdition && (
      <NewEditionDialog
        term={newEdition.term}
        onClose={() => setNewEdition(null)}
        onCreated={(editionId) => setValues((v) => ({ ...v, editionId }))}
      />
    )}
    </>
  );
}

/** The edition editor in create mode on top of the tenant editor; the saved edition joins the picker's list at once. */
function NewEditionDialog({ term, onClose, onCreated }: { term: string; onClose: () => void; onCreated: (editionId: string) => void }) {
  const queryClient = useQueryClient();
  return (
    <EditionFormDialog
      mode="create"
      initialName={term}
      canManageFeatures={false}
      open
      onClose={onClose}
      onSuccess={(saved) => {
        const id = saved?.id;
        if (!id) return;
        /* Shown at once; the refetch brings the server's list. */
        queryClient.setQueryData<EditionLookup[]>(["platform", "tenants", "edition-lookup"], (rows) =>
          rows && !rows.some((r) => r.id === id) ? [...rows, { id, displayName: saved.displayName ?? "" }] : rows,
        );
        void queryClient.invalidateQueries({ queryKey: ["platform", "tenants", "edition-lookup"] });
        onCreated(id);
      }}
    />
  );
}
