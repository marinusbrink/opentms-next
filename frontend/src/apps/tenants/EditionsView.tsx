import { lazy, Suspense, useCallback, useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { DateTimeCell, NumberCell } from "@/components/ui/grid-cells";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { Skeleton } from "@/components/ui/skeleton";
import type { GridRequest, GridRowAction, OpenTmsGridProps } from "@/components/ui/opentms-grid";
import { EditionFormDialog } from "@/apps/tenants/EditionFormDialog";
import { useDeleteEdition, useEditionsGridFetcher, type EditionRow } from "@/domains/platform/editions";
import { useIsGranted } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

const _OpenTmsGrid = lazy(() =>
  import("@/components/ui/opentms-grid").then((m) => ({ default: m.OpenTmsGrid })),
);

// Typed generic wrapper around the lazy component
function OpenTmsGrid<TRow>(props: OpenTmsGridProps<TRow>) {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  return <_OpenTmsGrid {...(props as any)} />;
}

type EditorState = { mode: "create" } | { mode: "edit"; edition: EditionRow; readOnly: boolean } | null;

export function EditionsView() {
  const { t } = useL();
  const canView = useIsGranted("Platform.Editions");
  const canCreate = useIsGranted("Platform.Editions.Create");
  const canUpdate = useIsGranted("Platform.Editions.Update");
  const canDelete = useIsGranted("Platform.Editions.Delete");
  const canManageFeatures = useIsGranted("Platform.Editions.ManageFeatures");

  const deleteEdition = useDeleteEdition();

  const [refreshKey, setRefreshKey] = useState(0);
  const [editor, setEditor] = useState<EditorState>(null);
  const [editionToDelete, setEditionToDelete] = useState<EditionRow | undefined>();

  const baseFetcher = useEditionsGridFetcher();
  /* A new function per refreshKey is what makes the grid fetch again. */
  const fetchEditions = useCallback(
    (req: GridRequest) => baseFetcher(req),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [baseFetcher, refreshKey],
  );
  const invalidateGrid = useCallback(() => setRefreshKey((k) => k + 1), []);

  /* A user who may manage features but not rename still opens the editor: its Features tab is the edit. */
  const openRecord = useCallback(
    (row: EditionRow) => setEditor({ mode: "edit", edition: row, readOnly: !canUpdate }),
    [canUpdate],
  );

  const commands = useMemo<AppCommandBarCommand[]>(
    () =>
      canCreate
        ? [
            {
              id: "new-edition",
              labelKey: "Editions:NewEdition",
              icon: Plus,
              isPrimary: true,
              onClick: () => setEditor({ mode: "create" }),
            },
          ]
        : [],
    [canCreate],
  );

  const rowActions = useCallback(
    (rows: EditionRow[]): GridRowAction[] => {
      const row = rows.length === 1 ? rows[0] : undefined;
      if (!row) return [];
      return [
        ...(canUpdate || canManageFeatures
          ? [{ id: "edit", labelKey: "Edit", icon: Pencil, onClick: () => openRecord(row) }]
          : []),
        ...(canDelete
          ? [{ id: "delete", labelKey: "Delete", icon: Trash2, destructive: true, onClick: () => setEditionToDelete(row) }]
          : []),
      ];
    },
    [canDelete, canManageFeatures, canUpdate, openRecord],
  );

  const columnDefs = useMemo<ColDef<EditionRow>[]>(
    () => [
      { field: "displayName", headerName: t("Editions:Name"), sortable: true, filter: false, flex: 1, minWidth: 200 },
      {
        field: "tenantCount",
        headerName: t("Editions:TenantCount"),
        cellRenderer: NumberCell,
        type: "rightAligned",
        sortable: true,
        filter: false,
        minWidth: 120,
      },
      {
        field: "creationTime",
        headerName: t("Editions:Created"),
        cellRenderer: DateTimeCell,
        sortable: true,
        filter: false,
        minWidth: 150,
      },
    ],
    [t],
  );

  if (!canView) {
    return <PermissionDenied />;
  }

  return (
    <div className="flex h-full flex-col">
      <AppCommandBar commands={commands} />

      <div className="min-h-0 flex-1 px-6 pb-6">
        <Suspense fallback={<Skeleton className="h-full w-full" />}>
          <OpenTmsGrid<EditionRow>
            gridId="platform.editions"
            columnDefs={columnDefs}
            fetchRows={fetchEditions}
            onOpen={openRecord}
            rowActions={rowActions}
          />
        </Suspense>
      </div>

      {editor && (
        <EditionFormDialog
          key={editor.mode === "edit" ? editor.edition.id : "new"}
          mode={editor.mode}
          edition={editor.mode === "edit" ? editor.edition : undefined}
          readOnly={editor.mode === "edit" && editor.readOnly}
          canManageFeatures={canManageFeatures}
          open
          onClose={() => setEditor(null)}
          onSuccess={invalidateGrid}
        />
      )}

      <ConfirmDialog
        open={editionToDelete != null}
        onOpenChange={(open) => {
          if (!open) setEditionToDelete(undefined);
        }}
        title={t("Editions:DeleteEdition")}
        description={t("Editions:DeleteEditionQuestion").replace("{0}", editionToDelete?.displayName ?? "")}
        confirmLabel={t("Delete")}
        busyLabel={t("Deleting")}
        destructive
        onConfirm={async () => {
          const result = await deleteEdition.mutateAsync(editionToDelete!.id!);
          if (result.status === "conflict") {
            /* Shown inside the open dialog (ConfirmDialog keeps failures there): move the tenants first. */
            throw {
              title: t("Editions:InUseDescription")
                .replace("{0}", result.displayName)
                .replace("{1}", String(result.tenantCount)),
            };
          }
          setEditionToDelete(undefined);
          toast.success(t("Editions:EditionDeleted"));
          invalidateGrid();
        }}
      />

    </div>
  );
}
