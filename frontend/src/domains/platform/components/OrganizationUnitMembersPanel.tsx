import { useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { Plus } from "lucide-react";

import { Button } from "@/components/ui/button";
import { BoolCell } from "@/components/ui/grid-cells";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { AddOrganizationUnitMembersDialog } from "@/domains/platform/components/AddOrganizationUnitMembersDialog";
import type { UserRow } from "@/domains/platform/administration-users";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface OrganizationUnitMembersPanelProps {
  unitId: string;
  unitName: string;
  /** The staged member list of the editor; applied on Save. */
  members: UserRow[];
  onChange: (members: UserRow[]) => void;
  /** Platform.Administration.OrganizationUnits.ManageMembers and the editor not read-only: shows Add and allows removal (UI-PERM-002). */
  canManage: boolean;
}

/* The Members tab of the organization-unit editor: the users in the unit as lines of the
 * record (UI-GRID-014): a client-side LinesGrid on the editor's state, Add members above it,
 * removal through the context menu and the trash icon without a confirm; Save applies the
 * difference, Close without saving drops it. */
export function OrganizationUnitMembersPanel({ unitId, unitName, members, onChange, canManage }: OrganizationUnitMembersPanelProps) {
  const { t } = useL();
  const [addOpen, setAddOpen] = useState(false);

  const memberIds = useMemo(() => new Set(members.map((m) => m.id ?? "")), [members]);

  const columnDefs = useMemo<ColDef<UserRow>[]>(
    () => [
      { field: "userName", headerName: t("Administration:Username"), sortable: true, filter: false, flex: 1, minWidth: 140 },
      { field: "email", headerName: t("Administration:Email"), sortable: true, filter: false, flex: 1, minWidth: 180 },
      {
        colId: "fullName",
        headerName: t("Administration:FullName"),
        valueGetter: (params) => [params.data?.name, params.data?.surname].filter(Boolean).join(" "),
        sortable: false,
        filter: false,
        flex: 1,
        minWidth: 140,
      },
      { field: "isActive", headerName: t("Administration:Active"), cellRenderer: BoolCell, sortable: false, filter: false, minWidth: 90 },
    ],
    [t],
  );

  return (
    <div className="flex h-96 flex-col gap-2">
      <LazyLinesGrid<UserRow>
        rows={members}
        onRowsChange={onChange}
        getRowId={(row) => row.id ?? ""}
        columnDefs={columnDefs}
        readOnly={!canManage}
        toolbar={
          <>
            <Button size="sm" variant="outline" onClick={() => setAddOpen(true)}>
              <Plus />
              {t("Administration:AddMembers")}
            </Button>
            <span className="text-label text-muted-foreground">{t("Administration:MembersPendingHint")}</span>
          </>
        }
      />

      {addOpen && (
        <AddOrganizationUnitMembersDialog
          unitId={unitId}
          unitName={unitName}
          excludeIds={memberIds}
          open
          onClose={() => setAddOpen(false)}
          onPicked={(users) => onChange([...members, ...users.filter((u) => !memberIds.has(u.id ?? ""))])}
        />
      )}
    </div>
  );
}
