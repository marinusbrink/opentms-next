import { useMemo, useRef, useState, type FormEvent } from "react";
import { Save } from "lucide-react";
import { toast } from "sonner";

import { useQueryClient } from "@tanstack/react-query";

import { Alert, AlertDescription } from "@/components/ui/alert";
import type { AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { AvatarPicker } from "@/components/ui/avatar-picker";
import { Checkbox } from "@/components/ui/checkbox";
import { EntityDialog, type EntityDialogTab } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { recordIdentificationTab } from "@/components/ui/record-identification-tab";
import { Skeleton } from "@/components/ui/skeleton";
import { buildTree } from "@/components/ui/tree-select";
import { TreeView } from "@/components/ui/tree-view";
import { useAllOrganizationUnits } from "@/domains/platform/administration-organization-units";
import { useAllRoles } from "@/domains/platform/administration-roles";
import { myProfileQueryKey } from "@/domains/platform/my-profile";
import {
  profilePictureQueryKey,
  useCreateUser,
  useDeleteProfilePicture,
  useProfilePictureUrl,
  useSetProfilePicture,
  useUpdateUser,
  type UserCreateDto,
  type UserRow,
  type UserUpdateDto,
} from "@/domains/platform/administration-users";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface UserFormDialogProps {
  mode: "create" | "edit";
  user?: UserRow;
  /** UI-PERM-004: view-only when the user may not update. */
  readOnly?: boolean;
  open: boolean;
  onClose: () => void;
  /** After a save; a create passes the user the API returned (a lookup's "Add new …" selects it, UI-LOOK-004). */
  onSuccess: (saved?: UserRow) => void;
  /** Create mode only: the user name a lookup's "Add new …" row was typed with (UI-LOOK-004). */
  initialName?: string;
}

interface UserFormValues {
  userName: string;
  email: string;
  name: string;
  surname: string;
  phoneNumber: string;
  isActive: boolean;
  shouldChangePasswordOnNextLogin: boolean;
  lockoutEnabled: boolean;
  roleNames: string[];
  organizationUnitIds: string[];
}

type FieldName = "userName" | "email" | "password";

function normalise(values: UserFormValues) {
  return {
    userName: values.userName.trim() || null,
    email: values.email.trim() || null,
    name: values.name.trim() || null,
    surname: values.surname.trim() || null,
    phoneNumber: values.phoneNumber.trim() || null,
    isActive: values.isActive,
    shouldChangePasswordOnNextLogin: values.shouldChangePasswordOnNextLogin,
    lockoutEnabled: values.lockoutEnabled,
    roleNames: [...values.roleNames].sort(),
    organizationUnitIds: [...values.organizationUnitIds].sort(),
  };
}

function initialsOf(name: string, surname: string, userName: string): string {
  const letters = [name.trim()[0], surname.trim()[0]].filter(Boolean).join("");
  return (letters || userName.trim().slice(0, 2)).toUpperCase();
}

/* The one user editor for create, edit and view (UI-DLG-009): General holds Account (picture
 * and identity fields) and Access (the sign-in switches),
 * Roles as a checkbox list of every role of the tenant (or the host), Organization units as a
 * checkbox tree of the tenant's units (PO decision 2026-10-10), then Record identification.
 * Save is the primary command; the dialog adds Close. Validation on blur and on submit,
 * messages under the field (UI-FORM-006); a failed save shows inside the dialog. */
export function UserFormDialog({ mode, user, readOnly = false, open, onClose, onSuccess, initialName }: UserFormDialogProps) {
  const { t } = useL();
  const queryClient = useQueryClient();
  const createUser = useCreateUser();
  const updateUser = useUpdateUser();
  const setPicture = useSetProfilePicture();
  const deletePicture = useDeleteProfilePicture();
  const storedPicture = useProfilePictureUrl(mode === "edit" ? user?.id : undefined, user?.hasProfilePicture === true);
  const roles = useAllRoles();
  /* The tree needs the units permission; without it the tab is hidden (UI-PERM-002) and the
   * user's memberships are sent back unchanged. */
  const canSeeUnits = useIsGranted("Platform.Administration.OrganizationUnits");
  const units = useAllOrganizationUnits();
  const formRef = useRef<HTMLFormElement>(null);

  const initial = useMemo<UserFormValues>(
    () => ({
      userName: mode === "edit" ? (user?.userName ?? "") : (initialName ?? ""),
      email: mode === "edit" ? (user?.email ?? "") : "",
      name: mode === "edit" ? (user?.name ?? "") : "",
      surname: mode === "edit" ? (user?.surname ?? "") : "",
      phoneNumber: mode === "edit" ? (user?.phoneNumber ?? "") : "",
      isActive: mode === "edit" ? (user?.isActive ?? true) : true,
      shouldChangePasswordOnNextLogin: mode === "edit" ? (user?.shouldChangePasswordOnNextLogin ?? false) : false,
      lockoutEnabled: mode === "edit" ? (user?.lockoutEnabled ?? true) : true,
      roleNames: mode === "edit" ? (user?.roleNames ?? []) : [],
      organizationUnitIds: mode === "edit" ? (user?.organizationUnitIds ?? []) : [],
    }),
    [mode, user, initialName],
  );

  const [values, setValues] = useState<UserFormValues>(initial);
  const [password, setPassword] = useState("");
  const [setRandomPassword, setSetRandomPassword] = useState(false);
  /* The picture change staged for Save: undefined = unchanged, null = remove, File = replace. */
  const [pendingPicture, setPendingPicture] = useState<File | null | undefined>(undefined);
  const [touched, setTouched] = useState<Partial<Record<FieldName, boolean>>>({});
  const [submitted, setSubmitted] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState("general");

  const isPending = createUser.isPending || updateUser.isPending || setPicture.isPending || deletePicture.isPending;
  const isDirty =
    JSON.stringify(normalise(values)) !== JSON.stringify(normalise(initial)) ||
    password !== "" ||
    setRandomPassword ||
    pendingPicture !== undefined;

  const errors: Partial<Record<FieldName, string>> = {};
  if (!values.userName.trim()) errors.userName = t("FieldRequired");
  if (!values.email.trim()) errors.email = t("FieldRequired");
  if (mode === "create" && !setRandomPassword && !password) errors.password = t("FieldRequired");

  const shownError = (field: FieldName) => (touched[field] || submitted ? errors[field] : undefined);
  const touch = (field: FieldName) => setTouched((prev) => ({ ...prev, [field]: true }));
  const set = <K extends keyof UserFormValues>(key: K, value: UserFormValues[K]) =>
    setValues((prev) => ({ ...prev, [key]: value }));

  const toggleRole = (roleName: string, checked: boolean) =>
    set("roleNames", checked ? [...values.roleNames, roleName] : values.roleNames.filter((r) => r !== roleName));
  const toggleUnit = (unitId: string, checked: boolean) =>
    set(
      "organizationUnitIds",
      checked ? [...values.organizationUnitIds, unitId] : values.organizationUnitIds.filter((id) => id !== unitId),
    );

  const handleSubmit = async (e?: FormEvent) => {
    e?.preventDefault();
    if (readOnly || isPending) return;
    setSubmitted(true);
    setServerError(null);

    const firstInvalid = (["userName", "email", "password"] as FieldName[]).find((f) => errors[f]);
    if (firstInvalid) {
      /* UI-FORM-006: switch to the tab with the error and focus the first invalid field. */
      setActiveTab("general");
      requestAnimationFrame(() => formRef.current?.querySelector<HTMLElement>(`#uf-${firstInvalid}`)?.focus());
      return;
    }

    try {
      let userId: string;
      let createdUser: UserRow | undefined;
      if (mode === "create") {
        const dto: UserCreateDto = {
          userName: values.userName.trim(),
          email: values.email.trim(),
          name: values.name.trim() || null,
          surname: values.surname.trim() || null,
          phoneNumber: values.phoneNumber.trim() || null,
          password: setRandomPassword ? null : password,
          setRandomPassword,
          shouldChangePasswordOnNextLogin: values.shouldChangePasswordOnNextLogin,
          lockoutEnabled: values.lockoutEnabled,
          roleNames: values.roleNames,
          organizationUnitIds: values.organizationUnitIds,
        };
        const created = await createUser.mutateAsync(dto);
        createdUser = (created ?? undefined) as UserRow | undefined;
        userId = created?.id ?? "";
      } else {
        const dto: UserUpdateDto = {
          userName: values.userName.trim(),
          email: values.email.trim(),
          name: values.name.trim() || null,
          surname: values.surname.trim() || null,
          phoneNumber: values.phoneNumber.trim() || null,
          isActive: values.isActive,
          shouldChangePasswordOnNextLogin: values.shouldChangePasswordOnNextLogin,
          lockoutEnabled: values.lockoutEnabled,
          roleNames: values.roleNames,
          organizationUnitIds: values.organizationUnitIds,
        };
        userId = user!.id!;
        await updateUser.mutateAsync({ id: userId, dto });
      }
      /* The picture is a separate upload; it follows the record, so the record is saved first. */
      if (pendingPicture instanceof File && userId) {
        await setPicture.mutateAsync({ id: userId, file: pendingPicture });
      } else if (pendingPicture === null && userId) {
        await deletePicture.mutateAsync(userId);
      }
      if (pendingPicture !== undefined && userId) {
        void queryClient.invalidateQueries({ queryKey: profilePictureQueryKey(userId) });
      }
      /* The user may be the signed-in user: the user menu shows their name and picture (design 63). */
      void queryClient.invalidateQueries({ queryKey: myProfileQueryKey });
      onSuccess(createdUser);
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

  const hasShownErrors = (["userName", "email", "password"] as FieldName[]).some((f) => shownError(f));

  const generalTab: EntityDialogTab = {
    value: "general",
    label: t("General"),
    hasError: hasShownErrors,
    content: (
      <form
        ref={formRef}
        onSubmit={(e) => void handleSubmit(e)}
        className="grid grid-cols-1 gap-4"
        noValidate
      >
        {serverError && (
          <Alert variant="destructive">
            <AlertDescription>{serverError}</AlertDescription>
          </Alert>
        )}

        {/* Two topics (UI-FORM-005): who the account is, and how it may sign in. */}
        <FormSection title={t("Administration:Account")}>
          <div className="flex flex-col gap-3">
            {/* The picture with the two name fields beside it; the account fields one per row below. */}
            <div className="flex items-start gap-4">
              <AvatarPicker
                currentUrl={storedPicture.url}
                pending={pendingPicture}
                onChange={setPendingPicture}
                initials={initialsOf(values.name, values.surname, values.userName)}
                label={t("Administration:ProfilePicture")}
                disabled={readOnly}
              />
              <div className="grid min-w-0 flex-1 grid-cols-1 gap-3">
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
            </div>

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
            <FormField label={t("Administration:PhoneNumber")} htmlFor="uf-phone">
              <Input
                id="uf-phone"
                type="tel"
                value={values.phoneNumber}
                onChange={(e) => set("phoneNumber", e.target.value)}
                maxLength={16}
                disabled={readOnly}
                autoComplete="off"
              />
            </FormField>
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
          </div>
        </FormSection>

        <FormSection title={t("Administration:Access")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            {mode === "create" && (
              <>
                <label className="col-span-2 flex items-center gap-2 text-sm">
                  <Checkbox
                    checked={setRandomPassword}
                    onCheckedChange={(checked) => {
                      setSetRandomPassword(checked === true);
                      if (checked === true) setPassword("");
                    }}
                  />
                  {t("Administration:SetRandomPassword")}
                </label>
                {!setRandomPassword && (
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
              </>
            )}

            <label className="col-span-2 flex items-center gap-2 text-sm">
              <Checkbox
                checked={values.shouldChangePasswordOnNextLogin}
                onCheckedChange={(checked) => set("shouldChangePasswordOnNextLogin", checked === true)}
                disabled={readOnly}
              />
              {t("Administration:ShouldChangePasswordOnNextLogin")}
            </label>

            {mode === "edit" && (
              <label className="col-span-2 flex items-center gap-2 text-sm">
                <Checkbox
                  checked={values.isActive}
                  onCheckedChange={(checked) => set("isActive", checked === true)}
                  disabled={readOnly}
                />
                {t("Administration:Active")}
              </label>
            )}

            <label className="col-span-2 flex items-center gap-2 text-sm">
              <Checkbox
                checked={values.lockoutEnabled}
                onCheckedChange={(checked) => set("lockoutEnabled", checked === true)}
                disabled={readOnly}
              />
              {t("Administration:LockoutEnabled")}
            </label>
          </div>
        </FormSection>

        {/* Enter submits a small editor (UI-KEY-001). */}
        <button type="submit" className="hidden" aria-hidden="true" tabIndex={-1} />
      </form>
    ),
  };

  const rolesTab: EntityDialogTab = {
    value: "roles",
    label: t("Administration:Roles"),
    content: (
      <FormSection title={t("Administration:Roles")}>
        {roles.isLoading ? (
          <Skeleton className="h-40 w-full" />
        ) : roles.isError ? (
          <Alert variant="destructive">
            <AlertDescription>{getApiErrorMessage(roles.error, t)}</AlertDescription>
          </Alert>
        ) : (roles.data?.length ?? 0) === 0 ? (
          <p className="text-sm text-muted-foreground">{t("Administration:NoRoles")}</p>
        ) : (
          <ul className="flex flex-col gap-1" aria-label={t("Administration:Roles")}>
        {roles.data!.map((role) => {
          const name = role.name ?? "";
          return (
            <li key={role.id}>
              <label className="flex items-center gap-2 rounded px-2 py-1.5 text-sm hover:bg-muted">
                <Checkbox
                  checked={values.roleNames.includes(name)}
                  onCheckedChange={(checked) => toggleRole(name, checked === true)}
                  disabled={readOnly}
                />
                {name}
              </label>
            </li>
          );
        })}
          </ul>
        )}
      </FormSection>
    ),
  };

  const unitTree = useMemo(
    () =>
      buildTree(
        units.data ?? [],
        (u) => u.id ?? "",
        (u) => u.parentId,
        (u) => u.displayName ?? "",
      ),
    [units.data],
  );

  const unitsTab: EntityDialogTab | null = canSeeUnits
    ? {
        value: "organization-units",
        label: t("Administration:OrganizationUnits"),
        content: (
          <FormSection title={t("Administration:OrganizationUnits")}>
            {units.isLoading ? (
              <Skeleton className="h-40 w-full" />
            ) : units.isError ? (
              <Alert variant="destructive">
                <AlertDescription>{getApiErrorMessage(units.error, t)}</AlertDescription>
              </Alert>
            ) : (
              <TreeView
                nodes={unitTree}
                checkedIds={new Set(values.organizationUnitIds)}
                onCheckedChange={toggleUnit}
                disabled={readOnly}
                emptyText={t("Administration:NoOrganizationUnits")}
                aria-label={t("Administration:OrganizationUnits")}
              />
            )}
          </FormSection>
        ),
      }
    : null;

  const tabs: EntityDialogTab[] = [
    generalTab,
    rolesTab,
    ...(unitsTab ? [unitsTab] : []),
    recordIdentificationTab(t, { id: mode === "edit" ? user?.id : undefined, creationTime: user?.creationTime }),
  ];

  return (
    <EntityDialog
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="editor"
      size="md"
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
