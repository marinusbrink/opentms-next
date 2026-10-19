import { useCallback, useMemo } from "react";

import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Separator } from "@/components/ui/separator";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { CheckboxField, TextField } from "@/apps/master-data/components/fields";
import { HtmlBodyField } from "@/apps/master-data/components/HtmlBodyField";
import { MasterDataEditor, type EditorTab, type FieldErrors } from "@/apps/master-data/components/MasterDataEditor";
import {
  EMAIL_TEMPLATE_PURPOSES,
  MAX_EMAIL_ADDRESS_FIELD_LENGTH,
  MAX_HTML_BODY_LENGTH,
  MAX_PLATFORM_NAME_LENGTH,
  MAX_SUBJECT_LENGTH,
  PLATFORM_CULTURES,
  cultureKey,
  emailTemplates,
  type EmailTemplateDto,
  type EmailTemplatePurpose,
} from "@/domains/platform/master-data";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The e-mail template editor (design 49, PO 2026-10-18: as the OpenTMS template, "as it is a mail template"): `lg`, one
 * tab General laid out like an e-mail, in the OpenTMS order: Name | Default; Purpose; Language (All languages or one
 * configured language); a divider; From | Reply-to; To; Cc; Bcc | Send me a copy; Subject; HTML body. One template is
 * one language, so there are no language tabs. The body is edited in HtmlBodyField (the RichTextEditor, Tiptap,
 * GAP-08). The Description stays in the DTO but has no input (OpenTMS had none); it
 * is sent back unchanged. The server parses every Scriban field and reports a syntax error with the field and line. */

/** The Language select's value for "All languages" (the DTO's null culture). */
const ALL_LANGUAGES = "all";

interface Values {
  name: string;
  isDefault: boolean;
  purpose: EmailTemplatePurpose;
  /** A configured culture, or ALL_LANGUAGES. */
  culture: string;
  senderAddress: string;
  replyTo: string;
  to: string;
  cc: string;
  bcc: string;
  sendCopyToSender: boolean;
  subject: string;
  htmlBody: string;
}

const initialValuesFor = (initialName: string | undefined) => (dto: EmailTemplateDto | undefined): Values => ({
  name: dto?.name ?? initialName ?? "",
  isDefault: dto?.isDefault ?? false,
  purpose: dto?.purpose ?? EMAIL_TEMPLATE_PURPOSES[0].value,
  culture: dto?.culture ? dto.culture.toLowerCase() : ALL_LANGUAGES,
  senderAddress: dto?.senderAddress ?? "",
  replyTo: dto?.replyTo ?? "",
  to: dto?.to ?? "",
  cc: dto?.cc ?? "",
  bcc: dto?.bcc ?? "",
  sendCopyToSender: dto?.sendCopyToSender ?? false,
  subject: dto?.subject ?? "",
  htmlBody: dto?.htmlBody ?? "",
});

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.name.trim()) errors.name = "Platform:Required";
  if (!v.subject.trim()) errors.subject = "Platform:Required";
  if (!v.htmlBody.trim()) errors.htmlBody = "Platform:Required";
  return errors;
};

const text = (value: string) => value.trim() || null;

export function EmailTemplateEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t } = useL();
  const initialValues = useMemo(() => initialValuesFor(mode === "create" ? initialName : undefined), [mode, initialName]);
  const detail = emailTemplates.useDetail(mode === "edit" ? id : undefined);
  const create = emailTemplates.useCreate();
  const update = emailTemplates.useUpdate();
  const remove = emailTemplates.useDelete();
  const canDelete = useIsGranted("Platform.EmailTemplates.Delete");

  const save = useCallback(
    async (v: Values, dto: EmailTemplateDto | undefined) => {
      const body = {
        name: v.name.trim(),
        purpose: v.purpose,
        culture: v.culture === ALL_LANGUAGES ? null : v.culture,
        isDefault: v.isDefault,
        senderAddress: text(v.senderAddress),
        replyTo: text(v.replyTo),
        to: text(v.to),
        cc: text(v.cc),
        bcc: text(v.bcc),
        sendCopyToSender: v.sendCopyToSender,
        subject: v.subject.trim(),
        htmlBody: v.htmlBody,
        /* No input for it (OpenTMS had none): kept as it is. */
        description: dto?.description ?? null,
      };
      return mode === "create"
        ? await create.mutateAsync(body)
        : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id],
  );

  const purposeItems = EMAIL_TEMPLATE_PURPOSES.map((p) => ({ value: p.value, label: t(p.key) }));
  const languageItems = [
    { value: ALL_LANGUAGES, label: t("Platform:AllLanguages") },
    ...PLATFORM_CULTURES.map((c) => ({ value: c as string, label: t(cultureKey(c)) })),
  ];

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "Platform:Tab.General",
      fields: ["name", "isDefault", "purpose", "culture", "senderAddress", "replyTo", "to", "cc", "bcc", "sendCopyToSender", "subject", "htmlBody"],
      render: (form) => (
        <FormSection title={t("Platform:EmailTemplate")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <TextField form={form} field="name" labelKey="Platform:Name" required maxLength={MAX_PLATFORM_NAME_LENGTH} />
            <CheckboxField form={form} field="isDefault" labelKey="Platform:IsDefault" />
            <FormField label={t("Platform:Purpose")} htmlFor={form.id("purpose")} required error={form.error("purpose")} floating="always" className="col-start-1">
              <Select
                items={purposeItems}
                value={form.values.purpose}
                onValueChange={(next) => {
                  if (next != null) form.set("purpose", next as EmailTemplatePurpose);
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
            <FormField label={t("Platform:Culture")} htmlFor={form.id("culture")} error={form.error("culture")} floating="always" className="col-start-1">
              <Select
                items={languageItems}
                value={form.values.culture}
                onValueChange={(next) => {
                  form.set("culture", (next as string | null) ?? ALL_LANGUAGES);
                  form.blur("culture");
                }}
                disabled={form.readOnly}
              >
                <SelectTrigger id={form.id("culture")} className="h-10 w-full bg-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {languageItems.map((item) => (
                    <SelectItem key={item.value} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </FormField>
            <Separator className="col-span-2 my-1" />
            <TextField form={form} field="senderAddress" labelKey="Platform:From" maxLength={MAX_EMAIL_ADDRESS_FIELD_LENGTH} />
            <TextField form={form} field="replyTo" labelKey="Platform:ReplyTo" maxLength={MAX_EMAIL_ADDRESS_FIELD_LENGTH} />
            <TextField form={form} field="to" labelKey="Platform:To" maxLength={MAX_EMAIL_ADDRESS_FIELD_LENGTH} hintKey="Platform:ToEmptyHint" className="col-span-2" />
            <TextField form={form} field="cc" labelKey="Platform:Cc" maxLength={MAX_EMAIL_ADDRESS_FIELD_LENGTH} className="col-span-2" />
            <TextField form={form} field="bcc" labelKey="Platform:Bcc" maxLength={MAX_EMAIL_ADDRESS_FIELD_LENGTH} />
            <CheckboxField form={form} field="sendCopyToSender" labelKey="Platform:SendMeACopy" />
            <TextField form={form} field="subject" labelKey="Platform:Subject" required maxLength={MAX_SUBJECT_LENGTH} className="col-span-2" />
            <div className="col-span-2">
              <HtmlBodyField
                id={form.id("htmlBody")}
                label={t("Platform:HtmlBody")}
                required
                value={form.values.htmlBody}
                maxLength={MAX_HTML_BODY_LENGTH}
                readOnly={form.readOnly}
                error={form.error("htmlBody")}
                onChange={(next) => form.set("htmlBody", next)}
                onBlur={() => form.blur("htmlBody")}
              />
            </div>
          </div>
        </FormSection>
      ),
    },
  ];

  return (
    <MasterDataEditor<Values, EmailTemplateDto>
      entityKey="EmailTemplate"
      resource="Platform"
      queryEntity={emailTemplates.entity}
      size="lg"
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
      deleteRecord={mode === "edit" && canDelete ? remove : undefined}
    />
  );
}
