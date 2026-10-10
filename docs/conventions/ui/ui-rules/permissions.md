# Permissions (UI-PERM)

**Load for:** any command, menu item, tab, view or editor.

**Library:** `useIsGranted(policy: string): boolean` from `lib/auth/permissions.ts`. It reads `useApplicationConfiguration().auth.grantedPolicies`. Feature flags: `useIsFeatureEnabled(name)` from the same file.

---

### UI-PERM-001 · MUST · Permission checks only through `useIsGranted`
Features don't read `grantedPolicies` or ABP configuration directly and don't define their own `useIsGranted`.
- **Check:** grep for `grantedPolicies` outside `lib/auth`.
- **Evidence:** inventory §13 (`useIsGranted` duplicated in 2 views).

### UI-PERM-002 · MUST · No permission means hidden, never disabled
Without the permission, the element is **not rendered**:
- commands, context-menu items, footer actions
- tabs, nav entries
- "Add new …" rows and edit pencils

`disabled` is never used to express a missing permission.
- **Check:** no `disabled={!isGranted…}` / `disabled: !can…`.
- **Evidence:** inventory §11 (hide in 40 of 41 permission checks; the one disabled case is an integration teaser); §13; D-28.

```ts
// ✅
...(canDelete ? [{ id: "delete", labelKey: "Delete", icon: Trash2, variant: "destructive", requiresSelection: true, onClick }] : [])
// ❌
{ id: "delete", labelKey: "Delete", disabled: !canDelete, disabledReasonKey: "NoPermission", onClick }
```

### UI-PERM-003 · MUST · Every action has a policy
| Action | Policy |
|---|---|
| create | `.Create` |
| save of an existing record | `.Update` |
| delete | `.Delete` |
| other actions | the policy the design names (`.Archive`, `.Approve`, …) |

The design lists them. An action with no policy in the design is a design gap: report it, don't ship it ungated.
- **Check:** each command / row action is wrapped in a `useIsGranted` condition.
- **Evidence:** inventory §11 (business screens had no action-level checks = gap; the new backend has per-action policies, e.g. `Platform.Administration.Users.Delete`); D-29.

### UI-PERM-004 · MUST · Without update permission an editor is read-only
- **If the user may view but not update,** the editor opens with all controls `disabled`, without Save / Save only, and with Close.
- **If the user may not view,** `useOpenRecord` does not open the dialog and shows `toast.error(t("PermissionDenied"))`.
- **On a list page,** a missing view permission shows the `PermissionDenied` panel (UI-PAGE-005).
- **Check:** editors take `readOnly` from `!useIsGranted(<Entity>.Update)` in edit mode.
- **Evidence:** inventory §11 (route guard + hidden actions); §13 (the PermissionDenied panel).

### UI-PERM-005 · MUST · `disabled` with a reason is for business state only
Use `disabled` + `disabledReasonKey` (tooltip) only when the action exists for this user but the record's state blocks it ("Order is already invoiced").
- **Check:** every `disabledReasonKey` refers to a state, not a permission.
- **Evidence:** inventory §11 (disabled states in G2 are business state: saving, invalid, order state); §13 (`disabledReasonKey`).

### UI-PERM-006 · MUST · Feature flags hide like permissions
An element behind a disabled feature flag is not rendered.
- **Check:** `useIsFeatureEnabled` guards, never `disabled`.
- **Evidence:** inventory §11 (feature flags hide-only, 3 uses).
