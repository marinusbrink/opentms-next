import { useCallback, useMemo } from "react";

import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { HtmlPreview } from "@/components/ui/html-preview";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { CheckboxField, TextAreaField, TextField } from "@/apps/master-data/components/fields";
import { MasterDataEditor, type EditorTab, type FieldErrors, type FormContext } from "@/apps/master-data/components/MasterDataEditor";
import {
  EMAIL_TEMPLATE_PURPOSES,
  MAX_EMAIL_ADDRESS_FIELD_LENGTH,
  MAX_HTML_BODY_LENGTH,
  MAX_PLATFORM_DESCRIPTION_LENGTH,
  MAX_PLATFORM_NAME_LENGTH,
  MAX_SUBJECT_LENGTH,
  PLATFORM_CULTURES,
  PLATFORM_DEFAULT_CULTURE,
  cultureKey,
  emailTemplates,
  type EmailTemplateContentInputDto,
  type EmailTemplateDto,
  type EmailTemplatePurpose,
  type PlatformCulture,
} from "@/domains/platform/master-data";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The e-mail template editor (design 49, UI design): `lg`; tab General (Name, Purpose, Default, Description), tab
 * Addresses (Sender address, Sender name, Reply-to, To, Cc, Bcc, Send copy to sender), and one tab per configured
 * language (English, Nederlands) with the Subject and the HTML body as monospace source next to a sandboxed preview
 * (GAP-08 stays open: no rich-text editor). The English content is required; a language tab left empty is sent as
 * no content row. The server parses every Scriban field and reports a syntax error with field, language and line. */

interface Values {
  name: string;
  purpose: EmailTemplatePurpose;
  isDefault: boolean;
  description: string;
  senderAddress: string;
  senderName: string;
  replyTo: string;
  to: string;
  cc: string;
  bcc: string;
  sendCopyToSender: boolean;
  subject_en: string;
  htmlBody_en: string;
  subject_nl: string;
  htmlBody_nl: string;
}

type SubjectField = `subject_${PlatformCulture}`;
type BodyField = `htmlBody_${PlatformCulture}`;
const subjectField = (culture: PlatformCulture) => `subject_${culture}` as SubjectField;
const bodyField = (culture: PlatformCulture) => `htmlBody_${culture}` as BodyField;

const ADDRESS_FIELDS = ["senderAddress", "senderName", "replyTo", "to", "cc", "bcc"] as const;

const initialValuesFor = (initialName: string | undefined) => (dto: EmailTemplateDto | undefined): Values => {
  const content = (culture: PlatformCulture) => dto?.contents?.find((c) => (c.culture ?? "").toLowerCase() === culture);
  return {
    name: dto?.name ?? initialName ?? "",
    purpose: dto?.purpose ?? EMAIL_TEMPLATE_PURPOSES[0].value,
    isDefault: dto?.isDefault ?? false,
    description: dto?.description ?? "",
    senderAddress: dto?.senderAddress ?? "",
    senderName: dto?.senderName ?? "",
    replyTo: dto?.replyTo ?? "",
    to: dto?.to ?? "",
    cc: dto?.cc ?? "",
    bcc: dto?.bcc ?? "",
    sendCopyToSender: dto?.sendCopyToSender ?? false,
    subject_en: content("en")?.subject ?? "",
    htmlBody_en: content("en")?.htmlBody ?? "",
    subject_nl: content("nl")?.subject ?? "",
    htmlBody_nl: content("nl")?.htmlBody ?? "",
  };
};

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.name.trim()) errors.name = "Platform:Required";
  for (const culture of PLATFORM_CULTURES) {
    const subject = v[subjectField(culture)].trim();
    const body = v[bodyField(culture)].trim();
    if (culture === PLATFORM_DEFAULT_CULTURE) {
      /* The default language's content is required (server: DefaultLanguageContentRequired). */
      if (!subject) errors[subjectField(culture)] = "Platform:DefaultLanguageContentRequired";
      if (!body) errors[bodyField(culture)] = "Platform:DefaultLanguageContentRequired";
    } else if (subject || body) {
      /* Another language is optional, but a started one needs both parts. */
      if (!subject) errors[subjectField(culture)] = "Platform:Required";
      if (!body) errors[bodyField(culture)] = "Platform:Required";
    }
  }
  return errors;
};

const text = (value: string) => value.trim() || null;

/** The HTML body as source: a monospace Textarea with a fixed height (UI-FORM-001: in a FormField). */
function HtmlBodyField({ form, field }: { form: FormContext<Values>; field: BodyField }) {
  const { t } = useL();
  return (
    <FormField label={t("Platform:HtmlBody")} htmlFor={form.id(field)} required={field === bodyField(PLATFORM_DEFAULT_CULTURE)} error={form.error(field)} floating="always">
      <Textarea
        id={form.id(field)}
        value={form.values[field]}
        maxLength={MAX_HTML_BODY_LENGTH}
        spellCheck={false}
        disabled={form.readOnly}
        onChange={(e) => form.set(field, e.target.value)}
        onBlur={() => form.blur(field)}
        className="h-96 resize-none font-mono text-label field-sizing-fixed"
      />
    </FormField>
  );
}

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
      /* One content row per language that has content; an empty language tab sends none. */
      const contents: EmailTemplateContentInputDto[] = PLATFORM_CULTURES.filter(
        (culture) => v[subjectField(culture)].trim() || v[bodyField(culture)].trim(),
      ).map((culture) => ({ culture, subject: v[subjectField(culture)].trim(), htmlBody: v[bodyField(culture)] }));
      const body = {
        name: v.name.trim(),
        purpose: v.purpose,
        isDefault: v.isDefault,
        description: text(v.description),
        senderAddress: text(v.senderAddress),
        senderName: text(v.senderName),
        replyTo: text(v.replyTo),
        to: text(v.to),
        cc: text(v.cc),
        bcc: text(v.bcc),
        sendCopyToSender: v.sendCopyToSender,
        contents,
      };
      return mode === "create"
        ? await create.mutateAsync(body)
        : await update.mutateAsync({ id: id!, dto: { ...body, concurrencyStamp: dto?.concurrencyStamp ?? "" } });
    },
    [create, update, mode, id],
  );

  const purposeItems = EMAIL_TEMPLATE_PURPOSES.map((p) => ({ value: p.value, label: t(p.key) }));

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "Platform:Tab.General",
      fields: ["name", "purpose", "isDefault", "description"],
      render: (form) => (
        <FormSection title={t("Platform:EmailTemplate")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <TextField form={form} field="name" labelKey="Platform:Name" required maxLength={MAX_PLATFORM_NAME_LENGTH} />
            <FormField label={t("Platform:Purpose")} htmlFor={form.id("purpose")} required error={form.error("purpose")} floating="always">
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
            <CheckboxField form={form} field="isDefault" labelKey="Platform:IsDefault" className="col-span-2" />
            <TextAreaField form={form} field="description" labelKey="Platform:Description" maxLength={MAX_PLATFORM_DESCRIPTION_LENGTH} className="col-span-2" />
          </div>
        </FormSection>
      ),
    },
    {
      value: "addresses",
      labelKey: "Platform:Tab.Addresses",
      fields: [...ADDRESS_FIELDS, "sendCopyToSender"],
      render: (form) => (
        <FormSection title={t("Platform:Tab.Addresses")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <TextField form={form} field="senderAddress" labelKey="Platform:SenderAddress" maxLength={MAX_EMAIL_ADDRESS_FIELD_LENGTH} />
            <TextField form={form} field="senderName" labelKey="Platform:SenderName" maxLength={MAX_EMAIL_ADDRESS_FIELD_LENGTH} />
            <TextField form={form} field="replyTo" labelKey="Platform:ReplyTo" maxLength={MAX_EMAIL_ADDRESS_FIELD_LENGTH} />
            <TextField form={form} field="to" labelKey="Platform:To" maxLength={MAX_EMAIL_ADDRESS_FIELD_LENGTH} hintKey="Platform:ToEmptyHint" />
            <TextField form={form} field="cc" labelKey="Platform:Cc" maxLength={MAX_EMAIL_ADDRESS_FIELD_LENGTH} />
            <TextField form={form} field="bcc" labelKey="Platform:Bcc" maxLength={MAX_EMAIL_ADDRESS_FIELD_LENGTH} />
            <CheckboxField form={form} field="sendCopyToSender" labelKey="Platform:SendCopyToSender" className="col-span-2" />
          </div>
        </FormSection>
      ),
    },
    ...PLATFORM_CULTURES.map(
      (culture): EditorTab<Values> => ({
        value: `content-${culture}`,
        labelKey: cultureKey(culture),
        fields: [subjectField(culture), bodyField(culture)],
        render: (form) => (
          <div className="grid grid-cols-2 gap-4">
            <FormSection title={t(cultureKey(culture))}>
              <div className="grid grid-cols-1 gap-3">
                <TextField
                  form={form}
                  field={subjectField(culture)}
                  labelKey="Platform:Subject"
                  required={culture === PLATFORM_DEFAULT_CULTURE}
                  maxLength={MAX_SUBJECT_LENGTH}
                />
                <HtmlBodyField form={form} field={bodyField(culture)} />
              </div>
            </FormSection>
            <FormSection title={t("Platform:Preview")} className="flex flex-col">
              <HtmlPreview html={form.values[bodyField(culture)]} title={`${t("Platform:Preview")} ${t(cultureKey(culture))}`} className="min-h-96 flex-1" />
            </FormSection>
          </div>
        ),
      }),
    ),
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
