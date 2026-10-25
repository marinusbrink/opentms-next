import { useCallback, useEffect, useMemo, useRef, useState, useLayoutEffect, type MutableRefObject } from "react";
import { TriangleAlert } from "lucide-react";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import type { RichTextEditorHandle } from "@/components/ui/rich-text-editor";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Separator } from "@/components/ui/separator";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import { EmailTemplateTagsPanel } from "@/apps/master-data/components/EmailTemplateTagsPanel";
import { CheckboxField, TextField } from "@/apps/master-data/components/fields";
import { HtmlBodyField } from "@/apps/master-data/components/HtmlBodyField";
import { MasterDataEditor, type EditorTab, type FieldErrors, type FormContext } from "@/apps/master-data/components/MasterDataEditor";
import {
  EMAIL_TEMPLATE_PURPOSES,
  MAX_EMAIL_ADDRESS_FIELD_LENGTH,
  MAX_HTML_BODY_LENGTH,
  MAX_PLATFORM_NAME_LENGTH,
  MAX_SUBJECT_LENGTH,
  PLATFORM_CULTURES,
  cultureKey,
  emailTemplates,
  useEmailTemplateDefaults,
  type EmailTemplateDefaults,
  type EmailTemplateDto,
  type EmailTemplatePurpose,
} from "@/domains/platform/master-data";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The e-mail template editor (design 54, PO 2026-10-18: as the OpenTMS template, "as it is a mail template"): `lg`, one
 * tab General laid out like an e-mail, in the OpenTMS order: Name | Default; Purpose; Language (All languages or one
 * configured language); a divider; From | Reply-to; To; Cc; Bcc | Send me a copy; Subject; HTML body. One template is
 * one language, so there are no language tabs. The body is edited in HtmlBodyField (the RichTextEditor, Tiptap,
 * GAP-08). The Description stays in the DTO but has no input (OpenTMS had none); it
 * is sent back unchanged. The server parses every Scriban field and reports a syntax error with the field and line.
 *
 * Tags and default content (PO 2026-10-20):
 * - On create, the purpose's defaults (From, Reply-to, Subject, HTML body) fill every field the user has not typed in,
 *   on open and again when the purpose or the language changes; a field is "automatic" until the user types in it
 *   (`edited`: the user's onChange or a tag insert, never a programmatic write). The defaults query is keyed by
 *   (purpose, culture), so a response is only ever applied for the selection it was asked for, and only to the fields
 *   still automatic when it arrives. The filled values are also the dirty baseline (`initialValues`), so an untouched
 *   new template closes without the discard prompt (UI-FORM-009). On edit nothing is filled.
 * - The Tags panel beside the fields inserts a tag at the cursor of the field last focused among From, Reply-to, To,
 *   Cc, Bcc, Subject and the body (by default the body); an insert counts as typing. Hidden in read-only mode.
 * - Both reach the form's values (held by MasterDataEditor) through its `applyValues`: one pending change at a time,
 *   applied once per key to the latest values. */

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

/** The fields the Tags panel can insert into. */
type InsertTarget = "senderAddress" | "replyTo" | "to" | "cc" | "bcc" | "subject" | "htmlBody";

interface CaretRestore {
  field: InputTarget;
  /** The value the input must show before the caret is placed. */
  value: string;
  caret: number;
}

/** Puts the caret behind an inserted tag after the commit that wrote the new value into the controlled input. */
function RestoreCaret({ inputs, pending }: { inputs: MutableRefObject<Partial<Record<InputTarget, HTMLInputElement | null>>>; pending: MutableRefObject<CaretRestore | null> }) {
  useLayoutEffect(() => {
    const wanted = pending.current;
    if (!wanted) return;
    const input = inputs.current[wanted.field];
    if (input && input.value === wanted.value) {
      input.setSelectionRange(wanted.caret, wanted.caret);
      pending.current = null;
    }
  });
  return null;
}
type InputTarget = Exclude<InsertTarget, "htmlBody">;

/** The fields the purpose's defaults fill (the DTO members of the same name). */
const DEFAULTED_FIELDS = ["senderAddress", "replyTo", "subject", "htmlBody"] as const satisfies readonly (keyof Values & keyof EmailTemplateDefaults)[];

const INPUT_MAX_LENGTH: Record<InputTarget, number> = {
  senderAddress: MAX_EMAIL_ADDRESS_FIELD_LENGTH,
  replyTo: MAX_EMAIL_ADDRESS_FIELD_LENGTH,
  to: MAX_EMAIL_ADDRESS_FIELD_LENGTH,
  cc: MAX_EMAIL_ADDRESS_FIELD_LENGTH,
  bcc: MAX_EMAIL_ADDRESS_FIELD_LENGTH,
  subject: MAX_SUBJECT_LENGTH,
};

/** Writes the defaults into the fields not in `edited`; a member the server did not send leaves its field alone. */
const applyDefaults = (values: Values, defaults: EmailTemplateDefaults, edited: ReadonlySet<string>): Values => {
  const next = { ...values };
  for (const field of DEFAULTED_FIELDS) {
    const value = defaults[field];
    if (typeof value === "string" && !edited.has(field)) next[field] = value;
  }
  return next;
};

const initialValuesFor =
  (initialName: string | undefined, defaults: EmailTemplateDefaults | undefined) =>
  (dto: EmailTemplateDto | undefined): Values => {
    const values: Values = {
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
    };
    /* Create: the defaults are the baseline, so an untouched new template is not dirty. */
    return dto == null && defaults ? applyDefaults(values, defaults, new Set()) : values;
  };

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.name.trim()) errors.name = "Platform:Required";
  if (!v.subject.trim()) errors.subject = "Platform:Required";
  if (!v.htmlBody.trim()) errors.htmlBody = "Platform:Required";
  return errors;
};

const text = (value: string) => value.trim() || null;

interface PendingChange {
  key: number;
  update: (values: Values) => Values;
}

let changeCounter = 0;

export function EmailTemplateEditor({ mode, id, readOnly, open, onClose, onSaved, initialName }: EntityEditorProps) {
  const { t } = useL();
  const detail = emailTemplates.useDetail(mode === "edit" ? id : undefined);
  const create = emailTemplates.useCreate();
  const update = emailTemplates.useUpdate();
  const remove = emailTemplates.useDelete();
  const canDelete = useIsGranted("Platform.EmailTemplates.Delete");

  /* The fields the user typed in (or inserted a tag into): a default never overwrites them. */
  const edited = useRef(new Set<string>());
  /* The outside change for the form's values, applied once per key (defaults arriving, a tag inserted into an input). */
  const [pending, setPending] = useState<PendingChange | undefined>(undefined);

  /* The selection the defaults are fetched for: the form's initial purpose and language, then what the user picks. */
  const [selection, setSelection] = useState<{ purpose: EmailTemplatePurpose; culture: string | null }>({
    purpose: EMAIL_TEMPLATE_PURPOSES[0].value,
    culture: null,
  });
  const defaults = useEmailTemplateDefaults(selection.purpose, selection.culture, mode === "create" && !readOnly && open);

  /* The defaults last applied: the baseline of the dirty compare. `defaults.data` belongs to the current
   * (purpose, culture) key, so an older response for another selection never reaches here; the fields edited by the
   * time the change runs are skipped there. */
  const [applied, setApplied] = useState<EmailTemplateDefaults | undefined>(undefined);
  useEffect(() => {
    if (mode !== "create" || defaults.data == null) return;
    const data = defaults.data;
    setApplied(data);
    setPending({ key: ++changeCounter, update: (values) => applyDefaults(values, data, edited.current) });
  }, [mode, defaults.data]);

  const initialValues = useMemo(
    () => initialValuesFor(mode === "create" ? initialName : undefined, mode === "create" ? applied : undefined),
    [mode, initialName, applied],
  );

  /* Where a clicked tag goes: the field last focused; the body until one is. */
  const target = useRef<InsertTarget>("htmlBody");
  const inputs = useRef<Partial<Record<InputTarget, HTMLInputElement | null>>>({});
  const body = useRef<RichTextEditorHandle | null>(null);
  const caretRestore = useRef<CaretRestore | null>(null);

  const insertTag = useCallback((snippet: string) => {
    if (!snippet) return;
    const field = target.current;
    if (field === "htmlBody") {
      /* The editor emits onChange for the insert, which marks the body edited like typing does. */
      body.current?.insertText(snippet);
      return;
    }
    const input = inputs.current[field];
    if (!input || input.disabled) return;
    const start = input.selectionStart ?? input.value.length;
    const end = input.selectionEnd ?? start;
    const next = input.value.slice(0, start) + snippet + input.value.slice(end);
    /* As the input's own maxLength: a too-long edit is refused, silently. */
    if (next.length > INPUT_MAX_LENGTH[field]) return;
    /* The value goes through the form (a direct write would be undone by React's next commit of the stale value);
     * the caret is put behind the tag by RestoreCaret once the committed value is the new text. */
    input.focus();
    caretRestore.current = { field, value: next, caret: start + snippet.length };
    edited.current.add(field);
    setPending({ key: ++changeCounter, update: (values) => ({ ...values, [field]: next }) });
  }, []);

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

  /* The defaults could not be loaded: the fields stay empty, and the user is told why (UI-FB-005, UI-FB-008). */
  const notice =
    mode === "create" && defaults.error != null ? (
      <Alert variant="warning" role="status">
        <TriangleAlert aria-hidden />
        <AlertDescription>{t("Platform:EmailTemplateDefaultsFailed").replace("{0}", getApiErrorMessage(defaults.error, t))}</AlertDescription>
      </Alert>
    ) : undefined;

  const focusTarget = (field: InsertTarget) => () => {
    target.current = field;
  };
  const inputRef = (field: InputTarget) => (el: HTMLInputElement | null) => {
    inputs.current[field] = el;
  };

  const tabs: EditorTab<Values>[] = [
    {
      value: "general",
      labelKey: "Platform:Tab.General",
      fields: ["name", "isDefault", "purpose", "culture", "senderAddress", "replyTo", "to", "cc", "bcc", "sendCopyToSender", "subject", "htmlBody"],
      render: (form) => {
        /* The user's typing marks a field edited; programmatic writes (the defaults) go through `applyValues` and do not. */
        const typed: FormContext<Values> = {
          ...form,
          set: (field, value) => {
            edited.current.add(field);
            form.set(field, value);
          },
        };
        return (
          /* UI-DLG-006: side by side (lg) the tab takes the panel's full height and the form column and the Tags panel
           * scroll on their own; stacked (narrow) the tab panel scrolls as a whole and neither is height-limited. */
          <div className="flex flex-col gap-4 lg:h-full lg:min-h-0 lg:flex-row">
            <FormSection title={t("Platform:EmailTemplate")} className="min-w-0 flex-1 lg:min-h-0 lg:overflow-y-auto">
              <div className="grid grid-cols-2 gap-x-4 gap-y-3">
                <TextField form={typed} field="name" labelKey="Platform:Name" required maxLength={MAX_PLATFORM_NAME_LENGTH} />
                <CheckboxField form={form} field="isDefault" labelKey="Platform:IsDefault" />
                <FormField label={t("Platform:Purpose")} htmlFor={form.id("purpose")} required error={form.error("purpose")} floating="always" className="col-start-1">
                  <Select
                    items={purposeItems}
                    value={form.values.purpose}
                    onValueChange={(next) => {
                      if (next != null) {
                        const purpose = next as EmailTemplatePurpose;
                        form.set("purpose", purpose);
                        setSelection((s) => (s.purpose === purpose ? s : { ...s, purpose }));
                      }
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
                      const culture = (next as string | null) ?? ALL_LANGUAGES;
                      form.set("culture", culture);
                      const selected = culture === ALL_LANGUAGES ? null : culture;
                      setSelection((s) => (s.culture === selected ? s : { ...s, culture: selected }));
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
                <TextField form={typed} field="senderAddress" labelKey="Platform:From" maxLength={MAX_EMAIL_ADDRESS_FIELD_LENGTH} inputRef={inputRef("senderAddress")} onFocus={focusTarget("senderAddress")} />
                <TextField form={typed} field="replyTo" labelKey="Platform:ReplyTo" maxLength={MAX_EMAIL_ADDRESS_FIELD_LENGTH} inputRef={inputRef("replyTo")} onFocus={focusTarget("replyTo")} />
                <TextField form={typed} field="to" labelKey="Platform:To" maxLength={MAX_EMAIL_ADDRESS_FIELD_LENGTH} hintKey="Platform:ToEmptyHint" className="col-span-2" inputRef={inputRef("to")} onFocus={focusTarget("to")} />
                <TextField form={typed} field="cc" labelKey="Platform:Cc" maxLength={MAX_EMAIL_ADDRESS_FIELD_LENGTH} className="col-span-2" inputRef={inputRef("cc")} onFocus={focusTarget("cc")} />
                <TextField form={typed} field="bcc" labelKey="Platform:Bcc" maxLength={MAX_EMAIL_ADDRESS_FIELD_LENGTH} inputRef={inputRef("bcc")} onFocus={focusTarget("bcc")} />
                <CheckboxField form={form} field="sendCopyToSender" labelKey="Platform:SendMeACopy" />
                <TextField form={typed} field="subject" labelKey="Platform:Subject" required maxLength={MAX_SUBJECT_LENGTH} className="col-span-2" inputRef={inputRef("subject")} onFocus={focusTarget("subject")} />
                {/* Focus anywhere in the body control (its toolbar included) makes the body the insert target. */}
                <div className="col-span-2" onFocus={focusTarget("htmlBody")}>
                  <HtmlBodyField
                    id={form.id("htmlBody")}
                    label={t("Platform:HtmlBody")}
                    required
                    value={form.values.htmlBody}
                    maxLength={MAX_HTML_BODY_LENGTH}
                    readOnly={form.readOnly}
                    error={form.error("htmlBody")}
                    onChange={(next) => typed.set("htmlBody", next)}
                    onBlur={() => form.blur("htmlBody")}
                    editorRef={body}
                  />
                </div>
              </div>
            </FormSection>
            <RestoreCaret inputs={inputs} pending={caretRestore} />
            {!form.readOnly && <EmailTemplateTagsPanel purpose={form.values.purpose} onInsert={insertTag} className="lg:h-full lg:min-h-0 lg:w-1/3 lg:shrink-0" />}
          </div>
        );
      },
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
      notice={notice}
      applyValues={pending}
      /* The body editor is tall: a fixed full-height dialog, the form column and the Tags panel scrolling inside it. */
      panelScroll
      deleteRecord={mode === "edit" && canDelete ? remove : undefined}
    />
  );
}
