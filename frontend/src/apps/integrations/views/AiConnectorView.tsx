import { useMemo, useRef, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { PowerOff } from "lucide-react";
import { toast } from "sonner";

import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { CopyButton } from "@/components/ui/copy-button";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { DateTimeCell } from "@/components/ui/grid-cells";
import { Input } from "@/components/ui/input";
import type { GridRowAction, GridSelectionDto } from "@/components/ui/opentms-grid";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { Skeleton } from "@/components/ui/skeleton";
import { StatusBadge, StatusCell } from "@/components/ui/status-badge";
import {
  AI_CONNECTOR_MANAGE,
  AI_CONNECTOR_USE,
  AI_CONNECTION_STATUSES,
  canSwitchOffAiConnection,
  useAcceptAiConnector,
  useAiConnectionsGridFetcher,
  useAiConnectorStatus,
  useSwitchOffAiConnection,
  useWithdrawAiConnector,
  type AiConnectionRow,
  type AiConnectorStatus,
} from "@/domains/integrations/ai-connector";
import { enumName } from "@/domains/integrations/order-intake";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { countText } from "@/lib/i18n/count-text";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* Integrations → AI connector (design 74 "UI design"; PO decision 6: every connected AI tool shows here with its status from the
 * server, so a tenant always knows which integrations are live, and it can be switched off here).
 *
 * DEVIATION(UI-PAGE-001): a status section sits between the command bar and the grid, approved in design 74 "UI deviations".
 * DEVIATION(UI-GRID-007): no onOpen, because a connection has no record dialog and the row shows all its fields, approved in
 * design 74 "UI deviations".
 * DEVIATION(UI-GRID-005): no search box, because GET /connections does not search, approved in design 74 "UI deviations".
 *
 * The feature Apps.Integrations.AiConnector is handled by the shell (FeatureUnavailable on the route, the nav node hidden).
 * Nothing updates optimistically: accepting, withdrawing and switching off wait for the server. */

const EMPTY_SELECTION: GridSelectionDto = { mode: "Explicit", explicitIds: [], filterRequest: null, excludedIds: [] };

type SwitchOffTarget = { ids: string[]; row?: AiConnectionRow };

export function AiConnectorView() {
  const canUse = useIsGranted(AI_CONNECTOR_USE);
  const canManage = useIsGranted(AI_CONNECTOR_MANAGE);
  if (!canUse && !canManage) return <PermissionDenied />;
  /* With either permission a user may switch off every row they see: the server limits Use to the user's own rows. */
  return <AiConnectorContent canManage={canManage} canSwitchOff />;
}

function AiConnectorContent({ canManage, canSwitchOff }: { canManage: boolean; canSwitchOff: boolean }) {
  const { t } = useL();
  const status = useAiConnectorStatus();
  const data = status.data;
  const accepted = data?.isAccepted === true;
  const [refreshKey, setRefreshKey] = useState(0);
  const fetchRows = useAiConnectionsGridFetcher(refreshKey);
  const [selection, setSelection] = useState<GridSelectionDto>(EMPTY_SELECTION);
  const [target, setTarget] = useState<SwitchOffTarget | null>(null);
  const switchOff = useSwitchOffAiConnection();

  const columns = useMemo<ColDef<AiConnectionRow>[]>(
    () => [
      {
        field: "status",
        headerName: t("Integrations:Status"),
        minWidth: 130,
        sortable: false,
        cellRenderer: (p: { value: number | undefined }) => <StatusCell entity="AiConnection" value={enumName(AI_CONNECTION_STATUSES, p.value)} />,
      },
      { field: "userName", headerName: t("AiConnector:Column.User"), flex: 1, minWidth: 160, sortable: false },
      { field: "toolName", headerName: t("AiConnector:Column.Tool"), flex: 1, minWidth: 120, sortable: false },
      { field: "connectedAt", headerName: t("AiConnector:Column.Connected"), minWidth: 150, cellRenderer: DateTimeCell },
      { field: "lastUsedAt", headerName: t("Integrations:LastUsedAt"), minWidth: 150, cellRenderer: DateTimeCell },
      { field: "revokedAt", headerName: t("AiConnector:Column.SwitchedOff"), minWidth: 150, cellRenderer: DateTimeCell },
    ],
    [t],
  );

  const selectedIds = selection.mode === "Explicit" ? selection.explicitIds : [];
  /* UI-GRID-008: the destructive row action is also a selection command. Not accepted: status only, so no commands. */
  const commands: AppCommandBarCommand[] =
    accepted && canSwitchOff
      ? [
          {
            id: "switch-off-selected",
            labelKey: "AiConnector:SwitchOff",
            icon: PowerOff,
            variant: "destructive",
            requiresSelection: true,
            selectionLabelKeys: { zero: "AiConnector:SwitchOff", one: "AiConnector:SwitchOff", many: "AiConnector:SwitchOffN" },
            disabled: selectedIds.length === 0,
            disabledReasonKey: "AiConnector:SelectConnections",
            onClick: () => setTarget({ ids: selectedIds }),
          },
        ]
      : [];

  const rowActions = (rows: AiConnectionRow[]): GridRowAction[] => {
    const switchable = rows.filter((row) => row.id != null && canSwitchOffAiConnection(row));
    if (!canSwitchOff || switchable.length === 0) return [];
    return [
      {
        id: "switch-off",
        labelKey: "AiConnector:SwitchOff",
        icon: PowerOff,
        destructive: true,
        multi: true,
        onClick: () => setTarget({ ids: switchable.map((row) => row.id!), row: switchable.length === 1 ? switchable[0] : undefined }),
      },
    ];
  };

  const switchOffTarget = async () => {
    if (!target) return;
    try {
      for (const id of target.ids) await switchOff.mutateAsync(id);
    } finally {
      /* Also after a failure halfway: the grid shows what the server has switched off so far. */
      setRefreshKey((key) => key + 1);
    }
    toast.success(countText(t, "AiConnector:SwitchedOffN", target.ids.length));
    setTarget(null);
    setSelection(EMPTY_SELECTION);
  };

  /* The last question stays while the dialog animates closed, so it never reads "0 connections". */
  const lastQuestion = useRef("");
  if (target) {
    lastQuestion.current = target.row
      ? t("AiConnector:SwitchOff.Question")
          .replace("{0}", target.row.toolName ?? "")
          .replace("{1}", target.row.userName ?? "")
      : countText(t, "AiConnector:SwitchOff.NQuestion", target.ids.length);
  }
  const question = lastQuestion.current;

  return (
    <div className="flex h-full flex-col" data-slot="ai-connector-view">
      <AppCommandBar commands={commands} selectionCount={selectedIds.length} />
      <div className="shrink-0 px-6 py-4">
        {data ? (
          <StatusSection status={data} canManage={canManage} onWithdrawn={() => setSelection(EMPTY_SELECTION)} />
        ) : status.isError ? (
          <Alert variant="destructive">
            <AlertDescription className="flex items-center justify-between gap-3">
              <span>{getApiErrorMessage(status.error, t)}</span>
              <Button variant="outline" size="sm" onClick={() => void status.refetch()}>
                {t("AiConnector:Retry")}
              </Button>
            </AlertDescription>
          </Alert>
        ) : (
          <Skeleton className="h-32 w-full" />
        )}
      </div>
      {/* The grid mounts with the status, so its empty text can name the connector URL. Not accepted: status only. */}
      {accepted && (
        <div className="min-h-0 flex-1 px-6 pb-6">
          <LazyOpenTmsGrid<AiConnectionRow>
            gridId="integrations.aiConnector.connections"
            columnDefs={columns}
            fetchRows={fetchRows}
            onSelectionChange={canSwitchOff ? setSelection : undefined}
            rowActions={rowActions}
            emptyMessage={t("AiConnector:NoConnections").replace("{0}", data?.connectorUrl ?? "")}
            hideSearch
          />
        </div>
      )}
      <ConfirmDialog
        open={target != null}
        onOpenChange={(next) => !next && setTarget(null)}
        title={t("AiConnector:SwitchOff.Title")}
        description={question}
        confirmLabel={t("AiConnector:SwitchOff")}
        busyLabel={t("AiConnector:SwitchingOff")}
        destructive
        onConfirm={switchOffTarget}
      />
    </div>
  );
}

/** The connector URL with its copy button, the acceptance, and Accept or Withdraw for a user with Manage. */
function StatusSection({ status, canManage, onWithdrawn }: { status: AiConnectorStatus; canManage: boolean; onWithdrawn: () => void }) {
  const { t } = useL();
  const f = useFormat();
  const [dialog, setDialog] = useState<"accept" | "withdraw" | null>(null);
  const accept = useAcceptAiConnector();
  const withdraw = useWithdrawAiConnector();
  const accepted = status.isAccepted === true;

  const acceptedAt = f.dateTime(status.acceptedAt);
  const acceptance = !accepted
    ? t(canManage ? "AiConnector:Status.NotAcceptedManage" : "AiConnector:Status.NotAcceptedUser")
    : status.acceptedByUserName
      ? t("AiConnector:Status.AcceptedBy").replace("{0}", status.acceptedByUserName).replace("{1}", acceptedAt)
      : t("AiConnector:Status.AcceptedOn").replace("{0}", acceptedAt);

  const dataLeaving = ["AiConnector:Connect.DataOrders", "AiConnector:Connect.DataTrips", "AiConnector:Connect.DataRelations", "AiConnector:Accept.DataMoney", "AiConnector:Accept.DataMetrics"];

  return (
    <FormSection title={t("AiConnector:Status.Title")}>
      <div className="grid grid-cols-2 gap-x-4 gap-y-3">
        <div className="flex items-start gap-2">
          <FormField label={t("AiConnector:ConnectorUrl")} htmlFor="ai-connector-url" className="min-w-0 flex-1">
            <Input id="ai-connector-url" value={status.connectorUrl ?? ""} readOnly className="font-mono" onFocus={(e) => e.currentTarget.select()} />
          </FormField>
          {status.connectorUrl && <CopyButton value={status.connectorUrl} label={t("Integrations:CopyValue").replace("{0}", t("AiConnector:ConnectorUrl"))} className="mt-2" />}
        </div>
        <div className="flex items-center justify-between gap-3">
          <div className="flex min-w-0 flex-col items-start gap-1 text-sm" data-slot="ai-connector-acceptance">
            <StatusBadge entity="AiConnectorSharing" state={accepted ? "Accepted" : "NotAccepted"} />
            <span className={cn(!accepted && "text-muted-foreground")}>{acceptance}</span>
          </div>
          {canManage &&
            (accepted ? (
              <Button variant="destructive" className="shrink-0" onClick={() => setDialog("withdraw")}>
                {t("AiConnector:Withdraw")}
              </Button>
            ) : (
              <Button className="shrink-0" onClick={() => setDialog("accept")}>
                {t("AiConnector:Accept")}
              </Button>
            ))}
        </div>
      </div>

      {canManage && (
        <>
          <ConfirmDialog
            open={dialog === "accept"}
            onOpenChange={(next) => !next && setDialog(null)}
            title={t("AiConnector:Accept.Title")}
            description={t("AiConnector:Accept.Description")}
            confirmLabel={t("AiConnector:Accept")}
            busyLabel={t("AiConnector:Accepting")}
            onConfirm={async () => {
              await accept.mutateAsync();
              setDialog(null);
              toast.success(t("AiConnector:Accepted"));
            }}
          >
            <div className="grid gap-3 text-sm" data-slot="ai-connector-accept-details">
              <div className="grid gap-1">
                <p className="font-medium">{t("AiConnector:Accept.DataHeading")}</p>
                <ul className="list-disc pl-4">
                  {dataLeaving.map((key) => (
                    <li key={key}>{t(key)}</li>
                  ))}
                </ul>
                <p className="text-muted-foreground">{t("AiConnector:Accept.DataLeft")}</p>
              </div>
              <div className="grid gap-1">
                <p className="font-medium">{t("AiConnector:Accept.VendorHeading")}</p>
                <p>{t("AiConnector:Accept.Vendor")}</p>
              </div>
              <div className="grid gap-1">
                <p className="font-medium">{t("AiConnector:Accept.AgreementHeading")}</p>
                <p>{t("AiConnector:Accept.Agreement")}</p>
              </div>
            </div>
          </ConfirmDialog>
          <ConfirmDialog
            open={dialog === "withdraw"}
            onOpenChange={(next) => !next && setDialog(null)}
            title={t("AiConnector:Withdraw.Title")}
            description={t("AiConnector:Withdraw.Question")}
            confirmLabel={t("AiConnector:Withdraw")}
            busyLabel={t("AiConnector:Withdrawing")}
            destructive
            onConfirm={async () => {
              await withdraw.mutateAsync();
              setDialog(null);
              onWithdrawn();
              toast.success(t("AiConnector:Withdrawn"));
            }}
          />
        </>
      )}
    </FormSection>
  );
}
