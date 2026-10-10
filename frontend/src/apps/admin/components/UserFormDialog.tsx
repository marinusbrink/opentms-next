import { useMemo, useRef, useState, type FormEvent } from "react";
import { Save } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import type { AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { Checkbox } from "@/components/ui/checkbox";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { RoleMultiSelect } from "@/components/ui/role-multi-select";
import {
  useCreateUser,
  useUpdateUser,
  type UserCreateDto,
  type UserRow,
  type UserUpdateDto,
} from "@/domains/platform/administration-users";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface UserFormDialogProps {
  mode: "create" | "edit";
  user?: UserRow;
  /** UI-PERM-004: view-only when the user may not update. */
  readOnly?: boolean;
  open: boolean;
  onClose: () => void;
  onSuccess: () => void;
}

interface UserFormValues {
  userName: string;
  email: string;
  name: string;
  surname: string;
  isActive: boolean;
  roleNames: string[];
}

type FieldName = "userName" | "email" | "password";

function normalise(values: UserFormValues) {
  return {
    userName: values.userName.trim() || null,
    email: values.email.trim() || null,
    name: values.name.trim() || null,
    surname: values.surname.trim() || null,
    isActive: values.isActive,
    roleNames: [...values.roleNames].sort(),
  };
}

/* The one user editor for create, edit and view (UI-DLG-009): an EntityDialog `editor`,
 * size `sm`, Save as the primary command; the dialog adds Close. Validation on blur and on
 * submit, messages under the field (UI-FORM-006); a failed save shows inside the dialog. */
export function UserFormDialog({ mode, user, readOnly = false, open, onClose, onSuccess }: UserFormDialogProps) {
  const { t } = useL();
  const createUser = useCreateUser();
  const updateUser = useUpdateUser();
  const formRef = useRef<HTMLFormElement>(null);

  const initial = useMemo<UserFormValues>(
    () => ({
      userName: mode === "edit" ? (user?.userName ?? "") : "",
      email: mode === "edit" ? (user?.email ?? "") : "",
      name: mode === "edit" ? (user?.name ?? "") : "",
      surname: mode === "edit" ? (user?.surname ?? "") : "",
      isActive: mode === "edit" ? (user?.isActive ?? true) : true,
      roleNames: mode === "edit" ? (user?.roleNames ?? []) : [],
    }),
    [mode, user],
  );

  const [values, setValues] = useState<UserFormValues>(initial);
  const [password, setPassword] = useState("");
  const [touched, setTouched] = useState<Partial<Record<FieldName, boolean>>>({});
  const [submitted, setSubmitted] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);

  const isPending = createUser.isPending || updateUser.isPending;
  const isDirty =
    JSON.stringify(normalise(values)) !== JSON.stringify(normalise(initial)) || password !== "";

  const errors: Partial<Record<FieldName, string>> = {};
  if (!values.userName.trim()) errors.userName = t("FieldRequired");
  if (!values.email.trim()) errors.email = t("FieldRequired");
  if (mode === "create" && !password) errors.password = t("FieldRequired");

  const shownError = (field: FieldName) => (touched[field] || submitted ? errors[field] : undefined);
  const touch = (field: FieldName) => setTouched((prev) => ({ ...prev, [field]: true }));
  const set = <K extends keyof UserFormValues>(key: K, value: UserFormValues[K]) =>
    setValues((prev) => ({ ...prev, [key]: value }));

  const handleSubmit = async (e?: FormEvent) => {
    e?.preventDefault();
    if (readOnly || isPending) return;
    setSubmitted(true);
    setServerError(null);

    const firstInvalid = (["userName", "email", "password"] as FieldName[]).find((f) => errors[f]);
    if (firstInvalid) {
      formRef.current?.querySelector<HTMLElement>(`#uf-${firstInvalid}`)?.focus();
      return;
    }

    try {
      if (mode === "create") {
        const dto: UserCreateDto = {
          userName: values.userName.trim(),
          email: values.email.trim(),
          name: values.name.trim() || null,
          surname: values.surname.trim() || null,
          password,
          roleNames: values.roleNames,
        };
        await createUser.mutateAsync(dto);
      } else {
        const dto: UserUpdateDto = {
          userName: values.userName.trim(),
          email: values.email.trim(),
          name: values.name.trim() || null,
          surname: values.surname.trim() || null,
          isActive: values.isActive,
          roleNames: values.roleNames,
        };
        await updateUser.mutateAsync({ id: user!.id!, dto });
      }
      onSuccess();
      onClose();
      toast.success(t("Administration:UserSaved"));
    } catch (err) {
      setServerError(getApiErrorMessage(err, t));
    }
  };

  const title =
    mode === "create"
      ? t("Administration:NewUser")
      : `${t("Administration:User")} ${user?.userName ?? ""}`;

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
      isDirty={isDirty}
      isBusy={isPending}
    >
      <form
        ref={formRef}
        onSubmit={(e) => void handleSubmit(e)}
        className="grid grid-cols-1 gap-3"
        noValidate
      >
        {serverError && (
          <Alert variant="destructive">
            <AlertDescription>{serverError}</AlertDescription>
          </Alert>
        )}

        <FormField label={t("Administration:Username")} htmlFor="uf-userName" required error={shownError("userName")}>
          <Input
            id="uf-userName"
            value={values.userName}
            onChange={(e) => set("userName", e.target.value)}
            onBlur={() => touch("userName")}
            disabled={readOnly}
            autoComplete="off"
          />
        </FormField>

        <FormField label={t("Administration:Email")} htmlFor="uf-email" required error={shownError("email")}>
          <Input
            id="uf-email"
            type="email"
            value={values.email}
            onChange={(e) => set("email", e.target.value)}
            onBlur={() => touch("email")}
            disabled={readOnly}
            autoComplete="off"
          />
        </FormField>

        <div className="grid grid-cols-2 gap-x-4 gap-y-3">
          <FormField label={t("Administration:FirstName")} htmlFor="uf-name">
            <Input id="uf-name" value={values.name} onChange={(e) => set("name", e.target.value)} disabled={readOnly} />
          </FormField>
          <FormField label={t("Administration:Surname")} htmlFor="uf-surname">
            <Input
              id="uf-surname"
              value={values.surname}
              onChange={(e) => set("surname", e.target.value)}
              disabled={readOnly}
            />
          </FormField>
        </div>

        {mode === "create" && (
          <FormField label={t("Administration:Password")} htmlFor="uf-password" required error={shownError("password")}>
            <Input
              id="uf-password"
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              onBlur={() => touch("password")}
              autoComplete="new-password"
            />
          </FormField>
        )}

        {mode === "edit" && (
          <label className="flex items-center gap-2 text-sm">
            <Checkbox
              checked={values.isActive}
              onCheckedChange={(checked) => set("isActive", checked === true)}
              disabled={readOnly}
            />
            {t("Administration:Active")}
          </label>
        )}

        <FormField label={t("Administration:Roles")} htmlFor="uf-roles">
          <RoleMultiSelect
            id="uf-roles"
            value={values.roleNames}
            onChange={(roleNames) => set("roleNames", roleNames)}
            disabled={readOnly}
          />
        </FormField>

        {/* Enter submits a small editor (UI-KEY-001). */}
        <button type="submit" className="hidden" aria-hidden="true" tabIndex={-1} />
      </form>
    </EntityDialog>
  );
}
