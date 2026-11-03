import { useEffect, useMemo, useState } from "react";
import { Save } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import type { AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { Checkbox } from "@/components/ui/checkbox";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { roleDisplayName } from "@/domains/platform/administration-roles";
import {
  applyGrantChange,
  isLocked,
  usePermissionGrants,
  useUpdatePermissionGrants,
  USER_PROVIDER,
  type Grants,
  type PermissionGrant,
  type PermissionGroup,
  type PermissionProvider,
} from "@/domains/platform/administration-permissions";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface PermissionsDialogProps {
  /** "R" for a role (key: the role name), "U" for a user's direct grants (key: the user id). */
  providerName: PermissionProvider;
  providerKey: string;
  /** Shown after "Permissions" in the title: the role's localized name or the user name. */
  subject: string;
  /** A role the system seeds (design 71): its grants can change, the dialog says what that means. */
  isPredefinedRole?: boolean;
  open: boolean;
  onClose: () => void;
}

/** Indent per permission depth; deeper levels than the trees have today use the last step. */
const DEPTH_INDENT = ["", "ps-6", "ps-12", "ps-18"] as const;

/* The permissions of one role or one user (docs/designs/73-role-permissions.md): the OpenTMS module groups as sections,
 * each a tree of permissions with an "All" checkbox. Save sends only what changed, in one request. In user mode only
 * the direct grants change; what the user holds through a role is shown checked and disabled. */
export function PermissionsDialog({
  providerName,
  providerKey,
  subject,
  isPredefinedRole = false,
  open,
  onClose,
}: PermissionsDialogProps) {
  const { t } = useL();
  const grantsQuery = usePermissionGrants(providerName, providerKey);
  const update = useUpdatePermissionGrants();
  const isUser = providerName === USER_PROVIDER;

  const initial = useMemo(() => {
    const values: Grants = {};
    for (const group of grantsQuery.data ?? []) {
      for (const p of group.permissions) values[p.name] = p.isGranted;
    }
    return values;
  }, [grantsQuery.data]);
  const [grants, setGrants] = useState<Grants>({});
  useEffect(() => setGrants(initial), [initial]);

  const [search, setSearch] = useState("");
  const [serverError, setServerError] = useState<string | null>(null);

  const changed = Object.entries(grants)
    .filter(([name, granted]) => initial[name] !== granted)
    .map(([name, isGranted]) => ({ name, isGranted }));
  const isDirty = changed.length > 0;

  const save = async () => {
    if (update.isPending) return;
    setServerError(null);
    if (!isDirty) {
      onClose();
      return;
    }
    try {
      await update.mutateAsync({ providerName, providerKey, permissions: changed });
      toast.success(t("Administration:PermissionsSaved"));
      onClose();
    } catch (err) {
      setServerError(getApiErrorMessage(err, t));
    }
  };

  const commands: AppCommandBarCommand[] = [
    {
      id: "save",
      labelKey: update.isPending ? "Saving" : "Save",
      icon: Save,
      isPrimary: true,
      disabled: grantsQuery.data == null,
      onClick: () => void save(),
    },
  ];

  const needle = search.trim().toLocaleLowerCase();
  const visibleGroups = (grantsQuery.data ?? [])
    .map((group) => ({
      group,
      permissions: needle
        ? group.permissions.filter((p) => p.displayName.toLocaleLowerCase().includes(needle))
        : group.permissions,
    }))
    .filter((entry) => entry.permissions.length > 0);

  return (
    <EntityDialog
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="editor"
      size="md"
      title={`${t("Administration:Permissions")} ${subject}`}
      commands={commands}
      isDirty={isDirty}
      isBusy={update.isPending}
    >
      <div className="grid grid-cols-1 gap-4">
        {serverError && (
          <Alert variant="destructive">
            <AlertDescription>{serverError}</AlertDescription>
          </Alert>
        )}
        {isPredefinedRole && (
          <Alert variant="warning">
            <AlertDescription>{t("Administration:PredefinedRolePermissionsNote")}</AlertDescription>
          </Alert>
        )}
        {isUser && (
          <Alert variant="warning">
            <AlertDescription>{t("Administration:UserPermissionsNote")}</AlertDescription>
          </Alert>
        )}
        <Input
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder={t("Administration:PermissionsSearch")}
          aria-label={t("Administration:PermissionsSearch")}
          className="bg-background"
        />
        {grantsQuery.isLoading && <Skeleton className="h-40 w-full" />}
        {grantsQuery.isError && (
          <Alert variant="destructive">
            <AlertDescription>{getApiErrorMessage(grantsQuery.error, t)}</AlertDescription>
          </Alert>
        )}
        {grantsQuery.data != null && visibleGroups.length === 0 && (
          <p className="text-sm text-muted-foreground">{t("Administration:PermissionsNone")}</p>
        )}
        {visibleGroups.map(({ group, permissions }) => (
          <PermissionGroupSection
            key={group.name}
            group={group}
            permissions={permissions}
            grants={grants}
            disabled={update.isPending}
            onChange={setGrants}
          />
        ))}
      </div>
    </EntityDialog>
  );
}

function PermissionGroupSection({
  group,
  permissions,
  grants,
  disabled,
  onChange,
}: {
  group: PermissionGroup;
  permissions: PermissionGrant[];
  grants: Grants;
  disabled: boolean;
  onChange: (grants: Grants) => void;
}) {
  const { t } = useL();
  const editable = group.permissions.filter((p) => !isLocked(p));
  const grantedCount = editable.filter((p) => grants[p.name]).length;
  const allId = `perm-all-${group.name}`;

  const setAll = (granted: boolean) => {
    const next = { ...grants };
    for (const p of editable) next[p.name] = granted;
    onChange(next);
  };

  return (
    <FormSection title={group.displayName}>
      <div className="grid grid-cols-1 gap-2">
        <label className="flex items-center gap-2 text-sm font-medium" htmlFor={allId}>
          <Checkbox
            id={allId}
            checked={editable.length > 0 && grantedCount === editable.length}
            indeterminate={grantedCount > 0 && grantedCount < editable.length}
            disabled={disabled || editable.length === 0}
            onCheckedChange={(checked) => setAll(checked === true)}
          />
          {t("Administration:PermissionsAll")}
        </label>
        {permissions.map((permission) => {
          const id = `perm-${permission.name.replace(/[^A-Za-z0-9_-]/g, "_")}`;
          const throughRole = permission.grantedThroughRoles.length > 0;
          const locked = isLocked(permission);
          /* Held through a role and not directly: shown checked, changed only on the role. */
          const checked = (grants[permission.name] ?? false) || (locked && throughRole);
          const indent = DEPTH_INDENT[Math.min(permission.depth, DEPTH_INDENT.length - 1)];
          return (
            <label key={permission.name} className={`flex items-start gap-2 text-sm ${indent}`} htmlFor={id}>
              <Checkbox
                id={id}
                checked={checked}
                disabled={disabled || locked}
                onCheckedChange={(next) => onChange(applyGrantChange(group, grants, permission.name, next === true))}
              />
              <span className="flex flex-col">
                <span>{permission.displayName}</span>
                {throughRole && (
                  <span className="text-label text-muted-foreground">
                    {t("Administration:GrantedThroughRole").replace(
                      "{0}",
                      permission.grantedThroughRoles.map((role) => roleDisplayName(role, t)).join(", "),
                    )}
                  </span>
                )}
              </span>
            </label>
          );
        })}
      </div>
    </FormSection>
  );
}
