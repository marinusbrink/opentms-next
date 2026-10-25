import { useMemo, useRef, type FormEvent } from "react";
import { Save } from "lucide-react";

import { EntityDialog, type RibbonCommand } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { TextField, emailError } from "@/components/record-editor/fields";
import type { FieldErrors } from "@/components/record-editor/MasterDataEditor";
import { useFormState } from "@/lib/forms/use-form-state";
import { CONTACT_PERSON_ROLES, type ContactPersonRole } from "@/domains/master-data/entities";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* A contact person of a relation (design 43, UI design: the Contact persons tab opens this dialog on
 * top of the relation editor). The line is staged in the editor's form state and reaches the API with
 * the relation's Save. Personal data: see the design's privacy note. */

/** One contact person as the relation editor holds it. */
export interface ContactRow {
  key: string;
  id: string | null;
  firstName: string;
  middleName: string;
  lastName: string;
  role: ContactPersonRole;
  jobTitle: string;
  department: string;
  email: string;
  phone: string;
  mobile: string;
}

let rowCounter = 0;

export function newContactRow(): ContactRow {
  return { key: `new-${++rowCounter}`, id: null, firstName: "", middleName: "", lastName: "", role: 0, jobTitle: "", department: "", email: "", phone: "", mobile: "" };
}

export function contactFullName(row: ContactRow): string {
  return [row.firstName, row.middleName, row.lastName].map((part) => part.trim()).filter(Boolean).join(" ");
}

const validate = (v: ContactRow): FieldErrors<ContactRow> => {
  const errors: FieldErrors<ContactRow> = {};
  if (!v.firstName.trim()) errors.firstName = "MasterData:FirstNameRequired";
  if (!v.lastName.trim()) errors.lastName = "MasterData:LastNameRequired";
  const email = emailError(v.email);
  if (email) errors.email = email;
  return errors;
};

export interface ContactPersonDialogProps {
  open: boolean;
  /** The line being edited; null creates one. */
  row: ContactRow | null;
  readOnly: boolean;
  onClose: () => void;
  onSave: (row: ContactRow) => void;
}

export function ContactPersonDialog({ open, row, readOnly, onClose, onSave }: ContactPersonDialogProps) {
  const { t } = useL();
  const formRef = useRef<HTMLFormElement>(null);
  const initial = useMemo(() => row ?? newContactRow(), [row]);
  const { form, isDirty, submit } = useFormState(initial, validate, readOnly);
  const roleItems = CONTACT_PERSON_ROLES.map((r) => ({ value: r.value, label: t(r.key) }));

  const handleSubmit = (e?: FormEvent) => {
    e?.preventDefault();
    if (readOnly) return;
    const values = submit(formRef.current);
    if (!values) return;
    onSave(values);
    onClose();
  };

  const commands: RibbonCommand[] = readOnly
    ? []
    : [{ id: "save", labelKey: "Save", icon: Save, isPrimary: true, onClick: () => formRef.current?.requestSubmit() }];

  return (
    <EntityDialog
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="editor"
      size="md"
      title={t(row ? "MasterData:EditContactPerson" : "MasterData:NewContactPerson")}
      commands={commands}
      isDirty={isDirty}
    >
      <form ref={formRef} onSubmit={handleSubmit} noValidate>
        <FormSection title={t("MasterData:ContactPerson")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <TextField form={form} field="firstName" labelKey="MasterData:FirstName" required maxLength={64} />
            <TextField form={form} field="middleName" labelKey="MasterData:MiddleName" maxLength={32} />
            <TextField form={form} field="lastName" labelKey="MasterData:LastName" required maxLength={64} />
            <FormField label={t("MasterData:Role")} htmlFor={form.id("role")} required floating="always">
              <Select items={roleItems} value={form.values.role} onValueChange={(next) => form.set("role", (next ?? 0) as ContactPersonRole)} disabled={form.readOnly}>
                <SelectTrigger id={form.id("role")} className="h-10 w-full bg-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {roleItems.map((item) => (
                    <SelectItem key={item.value} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </FormField>
            <TextField form={form} field="jobTitle" labelKey="MasterData:JobTitle" maxLength={64} />
            <TextField form={form} field="department" labelKey="MasterData:Department" maxLength={64} />
            <TextField form={form} field="email" labelKey="MasterData:Email" maxLength={256} className="col-span-2" />
            <TextField form={form} field="phone" labelKey="MasterData:Phone" maxLength={32} />
            <TextField form={form} field="mobile" labelKey="MasterData:Mobile" maxLength={32} />
          </div>
        </FormSection>
        <button type="submit" className="hidden" aria-hidden="true" tabIndex={-1} />
      </form>
    </EntityDialog>
  );
}
