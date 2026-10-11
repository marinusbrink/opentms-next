import { Alert, AlertDescription } from "@/components/ui/alert";
import { EntityDialog, type EntityDialogTab } from "@/components/ui/entity-dialog";
import { FormSection } from "@/components/ui/form-section";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { LogbookDetailRows } from "@/apps/admin/components/LogbookDetailRows";
import { useDatabaseDetail } from "@/domains/platform/administration-maintenance";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

interface DatabaseDetailDialogProps {
  databaseKey: string;
  open: boolean;
  onClose: () => void;
}

/* One database, read-only (UI-DLG-002 editor for viewing a record; UI-PERM-004: no Save, the
 * dialog adds Close). Tabs: the database and its blob store, its schemas, its largest tables. The
 * two tables are small and fixed (UI-GRID-001). */
export function DatabaseDetailDialog({ databaseKey, open, onClose }: DatabaseDetailDialogProps) {
  const { t } = useL();
  const f = useFormat();
  const query = useDatabaseDetail(databaseKey);
  const detail = query.data;

  const title = `${t("Maintenance:Database")} ${detail?.databaseName ?? ""}`.trim();

  const status = (
    <>
      {query.isError && (
        <Alert variant="destructive">
          <AlertDescription>{getApiErrorMessage(query.error, t)}</AlertDescription>
        </Alert>
      )}
      {query.isLoading && (
        <div className="grid gap-3">
          <Skeleton className="h-6 w-1/2" />
          <Skeleton className="h-6 w-2/3" />
          <Skeleton className="h-6 w-1/3" />
        </div>
      )}
    </>
  );

  const generalTab: EntityDialogTab = {
    value: "general",
    label: t("General"),
    content: (
      <div className="grid grid-cols-1 gap-4">
        {status}
        {detail && (
          <>
            <FormSection title={t("Maintenance:Database")}>
              <LogbookDetailRows
                rows={[
                  { label: t("Maintenance:DatabaseName"), value: detail.databaseName ?? "" },
                  { label: t("Maintenance:Server"), value: detail.serverName ?? "" },
                  { label: t("Maintenance:Role"), value: t(detail.isHost ? "Maintenance:RoleHost" : "Maintenance:RoleTenant") },
                  ...(detail.tenantName ? [{ label: t("Maintenance:Tenant"), value: detail.tenantName }] : []),
                  { label: t("Maintenance:Size"), value: f.bytes(detail.sizeBytes), tabular: true },
                  ...(detail.isHost
                    ? [{ label: t("Maintenance:SharedTenants"), value: (detail.sharedTenantNames ?? []).join(", ") }]
                    : []),
                ]}
              />
            </FormSection>

            <FormSection title={t("Maintenance:BlobStorage")}>
              {detail.blobStorage ? (
                <LogbookDetailRows
                  rows={[
                    { label: t("Maintenance:Blobs"), value: f.number(detail.blobStorage.blobCount), tabular: true },
                    { label: t("Maintenance:BlobContent"), value: f.bytes(detail.blobStorage.contentBytes), tabular: true },
                    { label: t("Maintenance:BlobTableSize"), value: f.bytes(detail.blobStorage.tableSizeBytes), tabular: true },
                  ]}
                />
              ) : (
                <p className="text-sm text-muted-foreground">{t("Maintenance:NoBlobStorage")}</p>
              )}
            </FormSection>
          </>
        )}
      </div>
    ),
  };

  const schemasTab: EntityDialogTab = {
    value: "schemas",
    label: t("Maintenance:Schemas"),
    content: (
      <div className="grid grid-cols-1 gap-4">
        {status}
        {detail && (
          <FormSection title={t("Maintenance:Schemas")}>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t("Maintenance:Schema")}</TableHead>
                  <TableHead className="text-right">{t("Maintenance:Size")}</TableHead>
                  <TableHead className="text-right">{t("Maintenance:Tables")}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {(detail.schemas ?? []).map((schema) => (
                  <TableRow key={schema.schemaName}>
                    <TableCell>{schema.schemaName}</TableCell>
                    <TableCell className="text-right tabular-nums">{f.bytes(schema.sizeBytes)}</TableCell>
                    <TableCell className="text-right tabular-nums">{f.number(schema.tableCount)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </FormSection>
        )}
      </div>
    ),
  };

  const tablesTab: EntityDialogTab = {
    value: "tables",
    label: t("Maintenance:LargestTables"),
    content: (
      <div className="grid grid-cols-1 gap-4">
        {status}
        {detail && (
          <FormSection title={t("Maintenance:LargestTables")}>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t("Maintenance:Schema")}</TableHead>
                  <TableHead>{t("Maintenance:Table")}</TableHead>
                  <TableHead className="text-right">{t("Maintenance:Size")}</TableHead>
                  <TableHead className="text-right">{t("Maintenance:LiveRows")}</TableHead>
                  <TableHead className="text-right">{t("Maintenance:DeadRows")}</TableHead>
                  <TableHead className="text-right">{t("Maintenance:SequentialScans")}</TableHead>
                  <TableHead className="text-right">{t("Maintenance:IndexScans")}</TableHead>
                  <TableHead>{t("Maintenance:LastVacuum")}</TableHead>
                  <TableHead>{t("Maintenance:LastAnalyze")}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {(detail.largestTables ?? []).map((table) => (
                  <TableRow key={`${table.schemaName}.${table.tableName}`}>
                    <TableCell>{table.schemaName}</TableCell>
                    <TableCell>{table.tableName}</TableCell>
                    <TableCell className="text-right tabular-nums">{f.bytes(table.totalSizeBytes)}</TableCell>
                    <TableCell className="text-right tabular-nums">{f.number(table.liveRows)}</TableCell>
                    <TableCell className="text-right tabular-nums">{f.number(table.deadRows)}</TableCell>
                    <TableCell className="text-right tabular-nums">{f.number(table.sequentialScans)}</TableCell>
                    <TableCell className="text-right tabular-nums">{f.number(table.indexScans)}</TableCell>
                    <TableCell className="tabular-nums">{f.timestamp(table.lastVacuumAt)}</TableCell>
                    <TableCell className="tabular-nums">{f.timestamp(table.lastAnalyzeAt)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </FormSection>
        )}
      </div>
    ),
  };

  return (
    <EntityDialog
      variant="editor"
      size="lg"
      title={title}
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      tabs={[generalTab, schemasTab, tablesTab]}
    />
  );
}
