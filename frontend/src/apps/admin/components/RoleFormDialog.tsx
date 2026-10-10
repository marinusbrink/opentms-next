import { useMemo, useRef, useState, type FormEvent } from "react";
import { Save } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import type { AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { Checkbox } from "@/components/ui/checkbox";
import { EntityDialog, type EntityDialogTab } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { recordIdentificationTab } from "@/components/ui/record-identification-tab";
import {
  useCreateRole,
  useUpdateRole,
  type RoleCreateUpdateDto,
  type RoleRow,
} from "@/domains/platform/administration-roles";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface RoleFormDialogProps {
  mode: "create" | "edit";
  role?: RoleRow;
  readOnly?: boolean;
  open: boolean;
  onClose: () => void;
  onSuccess: () => void;
}

interface RoleFormValues {
  name: string;
  isDefault: boolean;
  isPublic: boolean;
}

const normalise = (v: RoleFormValues) => ({ name: v.name.trim() || null, isDefault: v.isDefault, isPublic: v.isPublic });

export function RoleFormDialog({ mode, role, readOnly = false, open, onClose, onSuccess }: RoleFormDialogProps) {
  const { t } = useL();
  const createRole = useCreateRole();
  const updateRole = useUpdateRole();
  const formRef = useRef<HTMLFormElement>(null);

  const initial = useMemo<RoleFormValues>(
    () => ({
      name: mode === "edit" ? (role?.name ?? "") : "",
      isDefault: mode === "edit" ? (role?.isDefault ?? false) : false,
      isPublic: mode === "edit" ? (role?.isPublic ?? false) : false,
    }),
    [mode, role],
  );

  const [values, setValues] = useState<RoleFormValues>(initial);
  const [nameTouched, setNameTouched] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState("general");

  const isPending = createRole.isPending || updateRole.isPending;
  const isDirty = JSON.stringify(normalise(values)) !== JSON.stringify(normalise(initial));
  const nameError = values.name.trim() ? undefined : t("FieldRequired");
  const shownNameError = nameTouched || submitted ? nameError : undefined;

  const handleSubmit = async (e?: FormEvent) => {
    e?.preventDefault();
    if (readOnly || isPending) return;
    setSubmitted(true);
    setServerError(null);
    if (nameError) {
      setActiveTab("general");
      requestAnimationFrame(() => formRef.current?.querySelector<HTMLElement>("#rf-name")?.focus());
      return;
    }

    const dto: RoleCreateUpdateDto = {
      name: values.name.trim(),
      isDefault: values.isDefault,
      isPublic: values.isPublic,
    };
    try {
      if (mode === "create") {
        await createRole.mutateAsync(dto);
      } else {
        await updateRole.mutateAsync({ id: role!.id!, dto });
      }
      onSuccess();
      onClose();
      toast.success(t("Administration:RoleSaved"));
    } catch (err) {
      setServerError(getApiErrorMessage(err, t));
    }
  };

  const title =
    mode === "create" ? t("Administration:NewRole") : `${t("Administration:Role")} ${role?.name ?? ""}`;

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

  const generalTab: EntityDialogTab = {
    value: "general",
    label: t("General"),
    hasError: shownNameError != null,
    content: (
      <form ref={formRef} onSubmit={(e) => void handleSubmit(e)} className="grid grid-cols-1 gap-4" noValidate>
        {serverError && (
          <Alert variant="destructive">
            <AlertDescription>{serverError}</AlertDescription>
          </Alert>
        )}

        <FormField label={t("Administration:RoleName")} htmlFor="rf-name" required error={shownNameError}>
          <Input
            id="rf-name"
            value={values.name}
            onChange={(e) => setValues((v) => ({ ...v, name: e.target.value }))}
            onBlur={() => setNameTouched(true)}
            maxLength={256}
            disabled={readOnly}
          />
        </FormField>

        <div className="flex flex-col gap-2">
          <label className="flex items-center gap-2 text-sm">
            <Checkbox
              checked={values.isDefault}
              onCheckedChange={(checked) => setValues((v) => ({ ...v, isDefault: checked === true }))}
              disabled={readOnly}
            />
            {t("Administration:IsDefault")}
          </label>
          <label className="flex items-center gap-2 text-sm">
            <Checkbox
              checked={values.isPublic}
              onCheckedChange={(checked) => setValues((v) => ({ ...v, isPublic: checked === true }))}
              disabled={readOnly}
            />
            {t("Administration:IsPublic")}
          </label>
        </div>

        <button type="submit" className="hidden" aria-hidden="true" tabIndex={-1} />
      </form>
    ),
  };

  const tabs: EntityDialogTab[] = [
    generalTab,
    recordIdentificationTab(t, { id: mode === "edit" ? role?.id : undefined }),
  ];

  return (
    <EntityDialog
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="editor"
      size="sm"
      title={title}
      commands={commands}
      tabs={tabs}
      activeTab={activeTab}
      onActiveTabChange={setActiveTab}
      isDirty={isDirty}
      isBusy={isPending}
    />
  );
}
