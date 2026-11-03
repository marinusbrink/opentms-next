import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@/lib/api/client";

/* A role's or a user's grants (docs/designs/73-role-permissions.md), through ABP's own permission-management API:
 * provider "R" with the role name as key, provider "U" with the user id as key. ABP returns only the permissions of
 * the current side, with localized names, and refuses the call without the provider's ManagePermissions permission. */

export const ROLE_PROVIDER = "R";
export const USER_PROVIDER = "U";
export type PermissionProvider = typeof ROLE_PROVIDER | typeof USER_PROVIDER;

/**
 * The OpenTMS module groups the screen shows, one per backend module. ABP's own groups (AbpIdentity,
 * AbpTenantManagement, FeatureManagement, SettingManagement) have no OpenTMS screen behind them and stay hidden
 * (PO decision 6, design 73); their grants are never sent.
 */
export const VISIBLE_PERMISSION_GROUPS = [
  "Platform",
  "Orders",
  "PlanningExecution",
  "Financial",
  "MasterData",
  "Integrations",
  "Reporting",
] as const;

export type PermissionGrant = {
  name: string;
  displayName: string;
  parentName: string | null;
  /** Steps under the group's top level (0 for a top-level permission). */
  depth: number;
  /** Granted by this provider and key itself: the role's grant, or the user's direct grant. */
  isGranted: boolean;
  /** False when the caller may not change it (ABP: outside the admin role you change only what you hold yourself). */
  isEditable: boolean;
  /** User mode: the roles the user holds this permission through. */
  grantedThroughRoles: string[];
};

export type PermissionGroup = {
  name: string;
  displayName: string;
  permissions: PermissionGrant[];
};

type ProviderInfo = { providerName?: string | null; providerKey?: string | null };

/** Permission name → granted, as the dialog edits it. */
export type Grants = Record<string, boolean>;

/**
 * Not changeable here: ABP marks it not editable for the caller, or (user mode) the user holds it only through a role,
 * where it is changed. A direct grant the user also holds through a role stays changeable.
 */
export function isLocked(permission: PermissionGrant): boolean {
  return !permission.isEditable || (permission.grantedThroughRoles.length > 0 && !permission.isGranted);
}

/** Checking a permission checks its parents; unchecking one unchecks its children (ABP grants a child only with its parent). */
export function applyGrantChange(group: PermissionGroup, grants: Grants, name: string, granted: boolean): Grants {
  const next = { ...grants, [name]: granted };
  const byName = new Map(group.permissions.map((p) => [p.name, p]));
  if (granted) {
    let parent = byName.get(name)?.parentName ?? null;
    while (parent != null && byName.has(parent)) {
      if (!isLocked(byName.get(parent)!)) next[parent] = true;
      parent = byName.get(parent)!.parentName;
    }
  } else {
    const descendants = (of: string) => group.permissions.filter((p) => p.parentName === of);
    const stack = descendants(name);
    while (stack.length > 0) {
      const child = stack.pop()!;
      if (!isLocked(child)) next[child.name] = false;
      stack.push(...descendants(child.name));
    }
  }
  return next;
}

export function permissionGrantsQueryKey(providerName: PermissionProvider, providerKey: string | undefined) {
  return ["administration", "permissions", providerName, providerKey] as const;
}

/** Maps ABP's response to the visible groups, with each permission's depth in its tree and where its grant comes from. */
export function toPermissionGroups(
  groups: { name?: string | null; displayName?: string | null; permissions?: Array<{
    name?: string | null;
    displayName?: string | null;
    parentName?: string | null;
    isGranted?: boolean;
    isEditable?: boolean;
    grantedProviders?: ProviderInfo[] | null;
  }> | null }[],
  providerName: PermissionProvider,
): PermissionGroup[] {
  const visible = new Set<string>(VISIBLE_PERMISSION_GROUPS);
  return groups
    .filter((group) => visible.has(group.name ?? ""))
    .map((group) => {
      const parents = new Map<string, string | null>();
      for (const p of group.permissions ?? []) parents.set(p.name ?? "", p.parentName ?? null);
      const depthOf = (name: string): number => {
        let depth = 0;
        let parent = parents.get(name) ?? null;
        while (parent != null && parents.has(parent) && depth < 10) {
          depth += 1;
          parent = parents.get(parent) ?? null;
        }
        return depth;
      };
      return {
        name: group.name ?? "",
        displayName: group.displayName ?? group.name ?? "",
        permissions: (group.permissions ?? []).map((p) => {
          const providers = p.grantedProviders ?? [];
          const own = providers.some((g) => g.providerName === providerName);
          return {
            name: p.name ?? "",
            displayName: p.displayName ?? p.name ?? "",
            parentName: p.parentName ?? null,
            depth: depthOf(p.name ?? ""),
            /* A role's grant is its own; a user's isGranted also counts the roles, so read the direct grant. */
            isGranted: providerName === ROLE_PROVIDER ? (p.isGranted ?? false) : own,
            isEditable: p.isEditable ?? true,
            grantedThroughRoles:
              providerName === USER_PROVIDER
                ? providers.filter((g) => g.providerName === ROLE_PROVIDER).map((g) => g.providerKey ?? "")
                : [],
          };
        }),
      };
    })
    .filter((group) => group.permissions.length > 0);
}

export function usePermissionGrants(providerName: PermissionProvider, providerKey: string | undefined) {
  return useQuery({
    queryKey: permissionGrantsQueryKey(providerName, providerKey),
    enabled: providerKey != null && providerKey !== "",
    queryFn: async (): Promise<PermissionGroup[]> => {
      const { data, error } = await apiClient.GET("/api/permission-management/permissions", {
        params: { query: { providerName, providerKey } },
      });
      if (error) throw error;
      return toPermissionGroups(data?.groups ?? [], providerName);
    },
  });
}

export function useUpdatePermissionGrants() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({
      providerName,
      providerKey,
      permissions,
    }: {
      providerName: PermissionProvider;
      providerKey: string;
      permissions: { name: string; isGranted: boolean }[];
    }) => {
      const { error } = await apiClient.PUT("/api/permission-management/permissions", {
        params: { query: { providerName, providerKey } },
        body: { permissions },
      });
      if (error) throw error;
    },
    onSuccess: (_data, { providerName, providerKey }) => {
      void queryClient.invalidateQueries({ queryKey: permissionGrantsQueryKey(providerName, providerKey) });
      /* The signed-in user may hold the changed role: their granted policies come from the configuration. */
      void queryClient.invalidateQueries({ queryKey: ["abp", "application-configuration"] });
    },
  });
}
