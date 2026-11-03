import { useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { KeyRound, Pencil, Plus, Power, PowerOff, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { FeatureUnavailable } from "@/components/ui/feature-unavailable";
import { BoolCell, DateTimeCell } from "@/components/ui/grid-cells";
import type { GridRowAction, GridSelectionDto } from "@/components/ui/opentms-grid";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { ApiClientSecretDialog } from "@/apps/master-data/components/ApiClientSecretDialog";
import { ApiClientEditor } from "@/apps/master-data/editors/ApiClientEditor";
import {
  useActivateApiClient,
  useApiClientsGridFetcher,
  useDeactivateApiClient,
  useDeleteApiClient,
  useOrderIntakeFeature,
  useRegenerateApiClientSecret,
  type ApiClientRow,
  type ApiClientSecret,
} from "@/domains/integrations/order-intake";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { countText } from "@/lib/i18n/count-text";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Master data → Integrations → Order intake → API clients (design 72 "UI design"): name, client id, customer, active,
 * last used on a full-height server-side grid (UI-PAGE-001); New API client first (UI-ACT-001); the row actions Edit,
 * Regenerate secret, Deactivate / Activate and Delete (UI-GRID-008). A create or a regenerate shows the one-time secret
 * dialog. Order intake is a tenant feature (UI-PERM-006). */

const EMPTY_SELECTION: GridSelectionDto = { mode: "Explicit", explicitIds: [], filterRequest: null, excludedIds: [] };

type EditorState = { mode: "create" } | { mode: "edit"; id: string };

export function ApiClientsView() {
  const { t } = useL();
  const feature = useOrderIntakeFeature();
  const canView = useIsGranted("Integrations.ApiClients");
  const canManage = useIsGranted("Integrations.ApiClients.Manage");
  const [refreshKey, setRefreshKey] = useState(0);
  const filters = useMemo(() => ({}), []);
  const fetchRows = useApiClientsGridFetcher(filters, refreshKey);
  const [selection, setSelection] = useState<GridSelectionDto>(EMPTY_SELECTION);
  const [editor, setEditor] = useState<EditorState | null>(null);
  const [secret, setSecret] = useState<{ secret: ApiClientSecret; name: string } | null>(null);
  const [toDelete, setToDelete] = useState<{ ids: string[]; name?: string } | null>(null);
  const [toRegenerate, setToRegenerate] = useState<ApiClientRow | null>(null);
  const remove = useDeleteApiClient();
  const regenerate = useRegenerateApiClientSecret();
  const activate = useActivateApiClient();
  const deactivate = useDeactivateApiClient();
  const refresh = () => setRefreshKey((k) => k + 1);

  const columns = useMemo<ColDef<ApiClientRow>[]>(
    () => [
      { field: "name", headerName: t("Integrations:ApiClientName"), flex: 1, minWidth: 160 },
      { field: "clientId", headerName: t("Integrations:ClientId"), minWidth: 220, cellClass: "font-mono" },
      { field: "customerName", headerName: t("Integrations:Customer"), flex: 1, minWidth: 160, sortable: false },
      { field: "isActive", headerName: t("Integrations:IsActive"), minWidth: 90, cellRenderer: BoolCell },
      { field: "lastUsedAt", headerName: t("Integrations:LastUsedAt"), minWidth: 150, cellRenderer: DateTimeCell },
    ],
    [t],
  );

  if (feature === "off") return <FeatureUnavailable />;
  if (!canView) return <PermissionDenied />;

  const runRowAction = (action: () => Promise<unknown>, successKey: string) =>
    action().then(
      () => {
        toast.success(t(successKey));
        refresh();
      },
      (err: unknown) => toast.error(getApiErrorMessage(err, t)),
    );

  const selectedIds = selection.mode === "Explicit" ? selection.explicitIds : [];
  const commands: AppCommandBarCommand[] = canManage
    ? [
        { id: "new", labelKey: "Integrations:NewApiClient", icon: Plus, isPrimary: true, onClick: () => setEditor({ mode: "create" }) },
        {
          id: "delete-selected",
          labelKey: "Delete",
          icon: Trash2,
          variant: "destructive",
          requiresSelection: true,
          selectionLabelKeys: { zero: "Delete", one: "Delete", many: "Integrations:DeleteN" },
          disabled: selectedIds.length === 0,
          disabledReasonKey: "Integrations:SelectRows",
          onClick: () => setToDelete({ ids: selectedIds }),
        },
      ]
    : [];

  const rowActions = (rows: ApiClientRow[]): GridRowAction[] => {
    const row = rows.length === 1 ? rows[0] : undefined;
    const id = row?.id;
    if (!canManage) return id ? [{ id: "open", labelKey: "Open", onClick: () => setEditor({ mode: "edit", id }) }] : [];
    return [
      ...(id ? [{ id: "edit", labelKey: "Edit", icon: Pencil, onClick: () => setEditor({ mode: "edit", id }) }] : []),
      ...(id && row ? [{ id: "regenerate", labelKey: "Integrations:RegenerateSecret", icon: KeyRound, onClick: () => setToRegenerate(row) }] : []),
      ...(id && row?.isActive ? [{ id: "deactivate", labelKey: "Integrations:Deactivate", icon: PowerOff, onClick: () => void runRowAction(() => deactivate.mutateAsync(id), "Integrations:ApiClientDeactivated") }] : []),
      ...(id && row && !row.isActive ? [{ id: "activate", labelKey: "Integrations:Activate", icon: Power, onClick: () => void runRowAction(() => activate.mutateAsync(id), "Integrations:ApiClientActivated") }] : []),
      {
        id: "delete",
        labelKey: "Delete",
        icon: Trash2,
        destructive: true,
        multi: true,
        onClick: () => setToDelete({ ids: rows.map((r) => r.id).filter((value): value is string => value != null), name: row?.name ?? undefined }),
      },
    ];
  };

  const deleteRows = async () => {
    if (!toDelete) return;
    for (const id of toDelete.ids) {
      await remove.mutateAsync(id);
    }
    setToDelete(null);
    setSelection(EMPTY_SELECTION);
    toast.success(t("Integrations:ApiClientDeleted"));
    refresh();
  };

  return (
    <div className="flex h-full flex-col" data-slot="api-clients-view">
      <AppCommandBar commands={commands} selectionCount={selectedIds.length} />
      <div className="min-h-0 flex-1 px-6 pb-6">
        <LazyOpenTmsGrid<ApiClientRow>
          gridId="integrations.apiClients"
          columnDefs={columns}
          fetchRows={fetchRows}
          onSelectionChange={canManage ? setSelection : undefined}
          onOpen={(row) => row.id && setEditor({ mode: "edit", id: row.id })}
          rowActions={rowActions}
        />
      </div>
      {editor && (
        <ApiClientEditor
          key={editor.mode === "edit" ? editor.id : "new"}
          mode={editor.mode}
          id={editor.mode === "edit" ? editor.id : undefined}
          readOnly={!canManage}
          open
          onClose={() => setEditor(null)}
          onSaved={refresh}
          onSecret={(value, name) => setSecret({ secret: value, name })}
        />
      )}
      <ApiClientSecretDialog secret={secret?.secret ?? null} name={secret?.name ?? ""} onClose={() => setSecret(null)} />
      <ConfirmDialog
        open={toRegenerate != null}
        onOpenChange={(next) => !next && setToRegenerate(null)}
        title={t("Integrations:RegenerateSecret")}
        description={t("Integrations:RegenerateSecretQuestion").replace("{0}", toRegenerate?.name ?? "")}
        confirmLabel={t("Integrations:Regenerate")}
        busyLabel={t("Integrations:Regenerating")}
        destructive
        onConfirm={async () => {
          if (!toRegenerate?.id) return;
          const value = await regenerate.mutateAsync(toRegenerate.id);
          setSecret({ secret: value, name: toRegenerate.name ?? "" });
          setToRegenerate(null);
          refresh();
        }}
      />
      <ConfirmDialog
        open={toDelete != null}
        onOpenChange={(next) => !next && setToDelete(null)}
        title={t("Integrations:DeleteApiClient")}
        description={`${
          toDelete?.name != null
            ? t("Integrations:DeleteApiClientQuestion").replace("{0}", toDelete.name)
            : countText(t, "Integrations:DeleteNApiClientsQuestion", toDelete?.ids.length ?? 0)
        } ${t("ThisCannotBeUndone")}`}
        confirmLabel={t("Delete")}
        busyLabel={t("Deleting")}
        destructive
        onConfirm={deleteRows}
      />
    </div>
  );
}
