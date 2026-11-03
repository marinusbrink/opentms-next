import { useEffect, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { Mail, Pause, Pencil, Play, Plus, RefreshCw, Trash2 } from "lucide-react";
import { useLocation, useNavigate } from "@tanstack/react-router";
import { toast } from "sonner";

import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { FeatureUnavailable } from "@/components/ui/feature-unavailable";
import { DateTimeCell } from "@/components/ui/grid-cells";
import { MultiSelect } from "@/components/ui/multi-select";
import type { GridRowAction, GridSelectionDto } from "@/components/ui/opentms-grid";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { StatusCell } from "@/components/ui/status-badge";
import { ConnectMailboxDialog } from "@/apps/integrations/components/ConnectMailboxDialog";
import { MailboxEditor } from "@/apps/integrations/editors/MailboxEditor";
import {
  MAILBOX_STATUSES,
  MAIL_PROVIDERS,
  enumLabel,
  enumName,
  enumOptions,
  mailboxErrorText,
  useDeleteMailbox,
  useMailboxesGridFetcher,
  useOrderIntakeFeature,
  usePauseMailbox,
  useReconnectMailbox,
  useResumeMailbox,
  type MailProvider,
  type MailboxRow,
  type MailboxStatus,
} from "@/domains/integrations/order-intake";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { countText } from "@/lib/i18n/count-text";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Integrations → Order intake → Mailboxes (design 72 "UI design"): a command bar on a full-height server-side
 * grid (UI-PAGE-001) with Connect Gmail and Connect Microsoft 365 (each asks a display name, then goes to the provider),
 * the row actions Edit, Reconnect, Pause / Resume and Delete (UI-GRID-008), the status filter. The provider's sign-in
 * returns here: `?connected={id}` opens the editor on that connection (choose the folder), `?connectError={code}` shows
 * the localized refusal; both parameters are then removed. Order intake is a tenant feature (UI-PERM-006). */

const EMPTY_SELECTION: GridSelectionDto = { mode: "Explicit", explicitIds: [], filterRequest: null, excludedIds: [] };

export function MailboxesView() {
  const { t } = useL();
  const location = useLocation();
  const navigate = useNavigate();
  const feature = useOrderIntakeFeature();
  const canView = useIsGranted("Integrations.Mailboxes");
  const canManage = useIsGranted("Integrations.Mailboxes.Manage");
  const [statuses, setStatuses] = useState<MailboxStatus[]>([]);
  const [refreshKey, setRefreshKey] = useState(0);
  const filters = useMemo(() => ({ status: statuses }), [statuses]);
  const fetchRows = useMailboxesGridFetcher(filters, refreshKey);
  const [selection, setSelection] = useState<GridSelectionDto>(EMPTY_SELECTION);
  const [connecting, setConnecting] = useState<MailProvider | null>(null);
  const [editing, setEditing] = useState<string | null>(null);
  const [toDelete, setToDelete] = useState<{ ids: string[]; name?: string } | null>(null);
  const remove = useDeleteMailbox();
  const reconnect = useReconnectMailbox();
  const pause = usePauseMailbox();
  const resume = useResumeMailbox();
  const refresh = () => setRefreshKey((k) => k + 1);

  /* The OAuth return (design 72 "Connect"): open the connection, or say why it failed; then clean the URL. */
  const search = location.search as Record<string, unknown> | undefined;
  const connected = typeof search?.connected === "string" ? search.connected : undefined;
  const connectError = typeof search?.connectError === "string" ? search.connectError : undefined;
  useEffect(() => {
    if (feature !== "on" || (!connected && !connectError)) return;
    if (connected) setEditing(connected);
    if (connectError) {
      const key = `Mailboxes:ConnectError.${connectError}`;
      const text = t(key);
      toast.error(text === key ? t("Mailboxes:ConnectError.provider_error") : text);
    }
    void navigate({
      to: location.pathname,
      search: (previous: Record<string, unknown>) => Object.fromEntries(Object.entries(previous).filter(([key]) => key !== "connected" && key !== "connectError")),
      replace: true,
    });
  }, [feature, connected, connectError, navigate, location.pathname, t]);

  const columns = useMemo<ColDef<MailboxRow>[]>(
    () => [
      { field: "displayName", headerName: t("Integrations:MailboxName"), flex: 1, minWidth: 160 },
      { field: "provider", headerName: t("Integrations:Provider"), minWidth: 130, valueFormatter: (p) => enumLabel(t, "MailProvider", MAIL_PROVIDERS, p.value as number) },
      { field: "emailAddress", headerName: t("Integrations:EmailAddress"), flex: 1, minWidth: 180 },
      { field: "folderName", headerName: t("Integrations:Folder"), minWidth: 140 },
      { field: "status", headerName: t("Integrations:Status"), minWidth: 140, cellRenderer: (p: { value: number | undefined }) => <StatusCell entity="MailboxConnection" value={enumName(MAILBOX_STATUSES, p.value)} /> },
      { field: "lastSyncAt", headerName: t("Integrations:LastSyncAt"), minWidth: 150, cellRenderer: DateTimeCell },
      { colId: "lastErrorCode", field: "lastErrorCode", headerName: t("Integrations:LastError"), flex: 1, minWidth: 180, sortable: false, valueFormatter: (p) => mailboxErrorText(t, p.value as string | null) },
      { field: "lastErrorAt", headerName: t("Integrations:LastErrorAt"), minWidth: 150, cellRenderer: DateTimeCell, hide: true },
      { field: "creationTime", headerName: t("Integrations:CreationTime"), minWidth: 150, cellRenderer: DateTimeCell, hide: true },
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
        { id: "connect-gmail", labelKey: "Integrations:ConnectGmail", icon: Plus, isPrimary: true, onClick: () => setConnecting(1) },
        { id: "connect-microsoft", labelKey: "Integrations:ConnectMicrosoft365", icon: Mail, onClick: () => setConnecting(2) },
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

  const rowActions = (rows: MailboxRow[]): GridRowAction[] => {
    const row = rows.length === 1 ? rows[0] : undefined;
    const id = row?.id;
    const status = enumName(MAILBOX_STATUSES, row?.status);
    if (!canManage) return id ? [{ id: "open", labelKey: "Open", onClick: () => setEditing(id) }] : [];
    return [
      ...(id ? [{ id: "edit", labelKey: "Edit", icon: Pencil, onClick: () => setEditing(id) }] : []),
      ...(id && (status === "Reconnect" || status === "AwaitingConsent")
        ? [
            {
              id: "reconnect",
              labelKey: "Integrations:Reconnect",
              icon: RefreshCw,
              onClick: () =>
                void reconnect.mutateAsync(id).then(
                  (result) => result?.authorizeUrl && window.location.assign(result.authorizeUrl),
                  (err: unknown) => toast.error(getApiErrorMessage(err, t)),
                ),
            },
          ]
        : []),
      ...(id && status === "Active" ? [{ id: "pause", labelKey: "Integrations:Pause", icon: Pause, onClick: () => void runRowAction(() => pause.mutateAsync(id), "Integrations:MailboxPaused") }] : []),
      ...(id && status === "Paused" ? [{ id: "resume", labelKey: "Integrations:Resume", icon: Play, onClick: () => void runRowAction(() => resume.mutateAsync(id), "Integrations:MailboxResumed") }] : []),
      {
        id: "delete",
        labelKey: "Delete",
        icon: Trash2,
        destructive: true,
        multi: true,
        onClick: () => setToDelete({ ids: rows.map((r) => r.id).filter((value): value is string => value != null), name: row?.displayName ?? undefined }),
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
    toast.success(t("Integrations:MailboxDeleted"));
    refresh();
  };

  return (
    <div className="flex h-full flex-col" data-slot="mailboxes-view">
      <AppCommandBar commands={commands} selectionCount={selectedIds.length} />
      <div className="min-h-0 flex-1 px-6 pb-6">
        <LazyOpenTmsGrid<MailboxRow>
          gridId="integrations.mailboxes"
          columnDefs={columns}
          fetchRows={fetchRows}
          onSelectionChange={canManage ? setSelection : undefined}
          onOpen={(row) => row.id && setEditing(row.id)}
          rowActions={rowActions}
          filters={
            <MultiSelect
              id="mailboxes-status-filter"
              value={statuses.map(String)}
              onChange={(next) => setStatuses(next.map(Number) as MailboxStatus[])}
              options={enumOptions(t, "MailboxStatus", MAILBOX_STATUSES)}
              className="w-48"
            />
          }
        />
      </div>
      {editing && <MailboxEditor key={editing} id={editing} readOnly={!canManage} open onClose={() => setEditing(null)} onSaved={refresh} />}
      <ConnectMailboxDialog provider={connecting} onOpenChange={(next) => !next && setConnecting(null)} />
      <ConfirmDialog
        open={toDelete != null}
        onOpenChange={(next) => !next && setToDelete(null)}
        title={t("Integrations:DeleteMailbox")}
        description={`${
          toDelete?.name != null
            ? t("Integrations:DeleteMailboxQuestion").replace("{0}", toDelete.name)
            : countText(t, "Integrations:DeleteNMailboxesQuestion", toDelete?.ids.length ?? 0)
        } ${t("ThisCannotBeUndone")}`}
        confirmLabel={t("Delete")}
        busyLabel={t("Deleting")}
        destructive
        onConfirm={deleteRows}
      />
    </div>
  );
}
