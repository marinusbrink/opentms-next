import { useCallback, useMemo, useState } from "react";
import { Copy, ExternalLink, FileDown } from "lucide-react";
import { toast } from "sonner";

import { Badge } from "@/components/ui/badge";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import type { RibbonCommand } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { CheckboxField, TextAreaField, TextField } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import {
  MAX_FILE_NAME_PATTERN_LENGTH,
  MAX_PLATFORM_DESCRIPTION_LENGTH,
  MAX_PLATFORM_NAME_LENGTH,
  MAX_PROVIDER_TEMPLATE_ID_LENGTH,
  PLATFORM_CULTURES,
  PLATFORM_DEFAULT_CULTURE,
  REPORT_PURPOSES,
  REPORT_RENDERING_PROVIDERS,
  cultureKey,
  reportDefinitions,
  reportTemplateCommandBlock,
  useCopyReportTemplate,
  useOpenReportTemplateEditor,
  useReportRenderingStatus,
  useTestRenderReport,
  type ReportDefinitionDto,
  type ReportPurpose,
  type ReportRenderingProvider,
  type ReportTemplateCommand,
} from "@/domains/platform/master-data";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { openPendingTab } from "@/lib/browser";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The report definition editor (design 54, UI design): `md`; Name, Purpose, Language, Provider, Provider template id,
 * File name pattern (Scriban, ".pdf" added by the server when missing), Default, Archived, Available in customer
 * portal, Description. The server parses the pattern and keeps one default per purpose and language.
 *
 * The ribbon group Template (design 55, UI design), edit mode only: Edit template (the provider's editor in a new
 * tab), Copy template (confirmed; opens the new definition) and Test document (downloads the test PDF). Each needs its
 * permission (hidden without it, UI-PERM-002: EditTemplate; EditTemplate + Create; the view permission) and is
 * disabled with the reason from the rendering status (UI-PERM-005). They do not change this record, so they show in
 * read-only mode too. Under Provider template id the template's name at the provider, or "not found".
 *
 * DEVIATION(UI-PERM-006): feature Platform.DocumentRendering off disables the commands with the reason "feature off"
 * instead of hiding them, approved in docs/designs/55-report-rendering.md (UI design: "disabled with its reason ...
 * feature off"; Report-definition operations: the status reports the feature "so it can report 'feature off' as a
 * reason"). */

const TEMPLATE_GROUP = "Platform:TemplateGroup";

interface Values {
  name: string;
  purpose: ReportPurpose;
  culture: string;
  provider: ReportRenderingProvider;
  providerTemplateId: string;
  fileNamePattern: string;
  isDefault: boolean;
  isArchived: boolean;
  availableInCustomerPortal: boolean;
  description: string;
}

/** A new definition starts in the user's language when it is a configured one. */
const startCulture = (culture: string) => {
  const language = culture.toLowerCase().split("-")[0];
  return (PLATFORM_CULTURES as readonly string[]).includes(language) ? language : PLATFORM_DEFAULT_CULTURE;
};

const initialValuesFor = (culture: string, initialName: string | undefined) => (dto: ReportDefinitionDto | undefined): Values => ({
  name: dto?.name ?? initialName ?? "",
  purpose: dto?.purpose ?? REPORT_PURPOSES[0].value,
  culture: dto?.culture ?? startCulture(culture),
  provider: dto?.provider ?? REPORT_RENDERING_PROVIDERS[0].value,
  providerTemplateId: dto?.providerTemplateId ?? "",
  fileNamePattern: dto?.fileNamePattern ?? "",
  isDefault: dto?.isDefault ?? false,
  isArchived: dto?.isArchived ?? false,
  availableInCustomerPortal: dto?.availableInCustomerPortal ?? false,
  description: dto?.description ?? "",
});

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.name.trim()) errors.name = "Platform:Required";
  if (!v.providerTemplateId.trim()) errors.providerTemplateId = "Platform:Required";
  if (!v.fileNamePattern.trim()) errors.fileNamePattern = "Platform:Required";
  return errors;
};

export function ReportDefinitionEditor({ mode, id, readOnly, open, onClose, onSaved, initialName, onOpenRecord }: EntityEditorProps) {
  const { t, culture } = useL();
  const initialValues = useMemo(() => initialValuesFor(culture, mode === "create" ? initialName : undefined), [culture, mode, initialName]);
  const detail = reportDefinitions.useDetail(mode === "edit" ? id : undefined);
  const create = reportDefinitions.useCreate();
  const update = reportDefinitions.useUpdate();
  const remove = reportDefinitions.useDelete();
  const canDelete = useIsGranted("Platform.ReportDefinitions.Delete");

  /* ── Template commands (design 55) ── */
  const isEdit = mode === "edit" && id != null;
  const canView = useIsGranted("Platform.ReportDefinitions");
  const canCreate = useIsGranted("Platform.ReportDefinitions.Create");
  const canEditTemplate = useIsGranted("Platform.ReportDefinitions.EditTemplate");
  const status = useReportRenderingStatus(isEdit ? id : undefined);
  const openTemplateEditor = useOpenReportTemplateEditor();
  const copyTemplate = useCopyReportTemplate();
  const testRender = useTestRenderReport();
  const [commandError, setCommandError] = useState<string | null>(null);
  const [confirmCopy, setConfirmCopy] = useState(false);
  const isCommandBusy = openTemplateEditor.isPending || copyTemplate.isPending || testRender.isPending;
  const templateLabel = status.data?.templateName || detail.data?.providerTemplateId || "";

  /* The status describes the SAVED definition: archived and the template id are the loaded record's. While it loads
   * the commands wait; when it failed only unsaved changes block them and the server's answer to the command shows. */
  const templateCommand = (
    command: ReportTemplateCommand,
    labelKey: string,
    busyKey: string,
    icon: RibbonCommand["icon"],
    isPending: boolean,
    isDirty: boolean,
    onClick: () => void,
  ): RibbonCommand => {
    const reason = reportTemplateCommandBlock(command, status.data, detail.data?.isArchived === true, isDirty);
    /* A provider that failed to answer: its message follows the reason. */
    const message = reason === "Platform:RenderingUnavailableHint" ? status.data?.message : undefined;
    return {
      id: command,
      labelKey: isPending ? busyKey : labelKey,
      icon,
      groupKey: TEMPLATE_GROUP,
      disabled: detail.data == null || status.isLoading || reason != null,
      disabledReasonKey: reason,
      disabledReason: message ? `${t(reason!)} ${message}` : undefined,
      onClick,
    };
  };

  /* The tab opens inside the click (popup blockers allow it only there) and gets the URL after the POST. */
  const handleEditTemplate = async () => {
    if (!id) return;
    setCommandError(null);
    const tab = openPendingTab();
    try {
      tab.navigate(await openTemplateEditor.mutateAsync(id));
    } catch (err) {
      tab.close();
      setCommandError(getApiErrorMessage(err, t));
    }
  };

  /* A failure stays in the confirmation (UI-FB-004: where the user is); ConfirmDialog shows what this throws. */
  const handleCopyTemplate = async () => {
    if (!id) return;
    const copy = await copyTemplate.mutateAsync(id);
    setConfirmCopy(false);
    toast.success(t("Platform:TemplateCopied").replace("{0}", copy.name ?? ""));
    onSaved(copy);
    if (onOpenRecord && copy.id) onOpenRecord(copy.id);
    else onClose();
  };

  const handleTestDocument = async () => {
    if (!id) return;
    setCommandError(null);
    try {
      await testRender.mutateAsync({ id, fallbackName: detail.data?.name ?? "" });
    } catch (err) {
      setCommandError(getApiErrorMessage(err, t));
    }
  };

  /* The commands act on the SAVED template id: unsaved changes disable them ("Save your changes first"). */
  const actionCommands = ({ isDirty }: { isDirty: boolean }): RibbonCommand[] =>
    isEdit
      ? [
          ...(canEditTemplate
            ? [
                templateCommand("editTemplate", "Platform:EditTemplate", "Platform:OpeningTemplate", ExternalLink, openTemplateEditor.isPending, isDirty, () =>
                  void handleEditTemplate(),
                ),
              ]
            : []),
          ...(canEditTemplate && canCreate
            ? [
                templateCommand("copyTemplate", "Platform:CopyTemplate", "Platform:CopyingTemplate", Copy, copyTemplate.isPending, isDirty, () => {
                  setCommandError(null);
                  setConfirmCopy(true);
                }),
              ]
            : []),
          ...(canView
            ? [
                templateCommand("testDocument", "Platform:TestDocument", "Platform:GeneratingDocument", FileDown, testRender.isPending, isDirty, () =>
                  void handleTestDocument(),
                ),
              ]
            : []),
        ]
      : [];

  /* The provider's name for the saved template id; nothing while the user types another id, or when unknown. */
  const templateHint = (typedId: string): string | undefined => {
    if (!isEdit || !status.data || typedId.trim() !== (detail.data?.providerTemplateId ?? "")) return undefined;
    if (status.data.templateFound === true) return t("Platform:TemplateName").replace("{0}", templateLabel);
    if (status.data.templateFound === false) return t("Platform:TemplateNotFoundHint");
    return undefined;
  };

  const save = useCallback(
    async (v: Values, dto: ReportDefinitionDto | undefined) => {
      const body = {
        name: v.name.trim(),
        purpose: v.purpose,
        culture: v.culture,
        provider: v.provider,
        providerTemplateId: v.providerTemplateId.trim(),
        fileNamePattern: v.fileNamePattern.trim(),
        isDefault: v.isDefault,
        isArchived: v.isArchived,
        availableInCustomerPortal: v.availableInCustomerPortal,
        description: v.description.trim() || null,
      };
      return mode === "create"
        ? await create.mutateAsync(body)
        : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id],
  );

  const purposeItems = REPORT_PURPOSES.map((p) => ({ value: p.value, label: t(p.key) }));
  const providerItems = REPORT_RENDERING_PROVIDERS.map((p) => ({ value: p.value, label: t(p.key) }));
  const cultureItems = PLATFORM_CULTURES.map((c) => ({ value: c as string, label: t(cultureKey(c)) }));

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "Platform:Tab.General",
      fields: ["name", "purpose", "culture", "provider", "providerTemplateId", "fileNamePattern", "isDefault", "isArchived", "availableInCustomerPortal", "description"],
      render: (form) => (
        <FormSection title={t("Platform:ReportDefinition")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <TextField form={form} field="name" labelKey="Platform:Name" required maxLength={MAX_PLATFORM_NAME_LENGTH} className="col-span-2" />
            <FormField label={t("Platform:Purpose")} htmlFor={form.id("purpose")} required error={form.error("purpose")} floating="always">
              <Select
                items={purposeItems}
                value={form.values.purpose}
                onValueChange={(next) => {
                  if (next != null) form.set("purpose", next as ReportPurpose);
                  form.blur("purpose");
                }}
                disabled={form.readOnly}
              >
                <SelectTrigger id={form.id("purpose")} className="h-10 w-full bg-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {purposeItems.map((item) => (
                    <SelectItem key={item.value} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </FormField>
            <FormField label={t("Platform:Culture")} htmlFor={form.id("culture")} required error={form.error("culture")} floating="always">
              <Select
                items={cultureItems}
                value={form.values.culture}
                onValueChange={(next) => {
                  if (next != null) form.set("culture", next as string);
                  form.blur("culture");
                }}
                disabled={form.readOnly}
              >
                <SelectTrigger id={form.id("culture")} className="h-10 w-full bg-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {cultureItems.map((item) => (
                    <SelectItem key={item.value} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </FormField>
            <FormField label={t("Platform:Provider")} htmlFor={form.id("provider")} required error={form.error("provider")} floating="always">
              <Select
                items={providerItems}
                value={form.values.provider}
                onValueChange={(next) => {
                  if (next != null) form.set("provider", next as ReportRenderingProvider);
                  form.blur("provider");
                }}
                disabled={form.readOnly}
              >
                <SelectTrigger id={form.id("provider")} className="h-10 w-full bg-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {providerItems.map((item) => (
                    <SelectItem key={item.value} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </FormField>
            <TextField
              form={form}
              field="providerTemplateId"
              labelKey="Platform:ProviderTemplateId"
              required
              maxLength={MAX_PROVIDER_TEMPLATE_ID_LENGTH}
              hint={templateHint(form.values.providerTemplateId)}
            />
            <TextField
              form={form}
              field="fileNamePattern"
              labelKey="Platform:FileNamePattern"
              required
              maxLength={MAX_FILE_NAME_PATTERN_LENGTH}
              hintKey="Platform:FileNamePatternHint"
              className="col-span-2"
            />
            <CheckboxField form={form} field="isDefault" labelKey="Platform:IsDefault" />
            <CheckboxField form={form} field="isArchived" labelKey="Platform:IsArchived" />
            <CheckboxField form={form} field="availableInCustomerPortal" labelKey="Platform:AvailableInCustomerPortal" className="col-span-2" />
            <TextAreaField form={form} field="description" labelKey="Platform:Description" maxLength={MAX_PLATFORM_DESCRIPTION_LENGTH} className="col-span-2" />
          </div>
        </FormSection>
      ),
    },
  ];

  return (
    <>
      <MasterDataEditor<Values, ReportDefinitionDto>
        entityKey="ReportDefinition"
        resource="Platform"
        queryEntity={reportDefinitions.entity}
        size="md"
        mode={mode}
        readOnly={readOnly}
        open={open}
        onClose={onClose}
        onSaved={onSaved}
        detail={detail}
        titleOf={(dto) => dto.name ?? ""}
        initialValues={initialValues}
        validate={validate}
        save={save}
        isSaving={create.isPending || update.isPending}
        tabs={tabs}
        actionCommands={actionCommands}
        isCommandBusy={isCommandBusy}
        commandError={commandError}
        deleteRecord={mode === "edit" && canDelete ? remove : undefined}
        badges={mode === "edit" && detail.data?.isArchived ? <Badge variant="outline">{t("Platform:IsArchived")}</Badge> : undefined}
      />
      <ConfirmDialog
        open={confirmCopy}
        onOpenChange={(next) => {
          if (!next) setConfirmCopy(false);
        }}
        title={t("Platform:CopyTemplate")}
        description={t("Platform:CopyTemplateQuestion").replace("{0}", templateLabel)}
        confirmLabel={t("Platform:Copy")}
        busyLabel={t("Platform:CopyingTemplate")}
        onConfirm={handleCopyTemplate}
      />
    </>
  );
}
