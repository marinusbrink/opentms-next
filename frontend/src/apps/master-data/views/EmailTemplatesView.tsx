import { useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";

import { BoolCell } from "@/components/ui/grid-cells";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { EmailTemplateEditor } from "@/apps/master-data/editors/EmailTemplateEditor";
import { MasterDataListView } from "@/apps/master-data/components/MasterDataListView";
import { EMAIL_TEMPLATE_PURPOSES, cultureKey, emailTemplatePurposeKey, emailTemplates, type EmailTemplatePurpose, type EmailTemplateRow } from "@/domains/platform/master-data";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The E-mail templates list (design 49, UI design): Name, Purpose, Language (one template per language, PO
 * 2026-10-18; an empty language is "All languages"), Default. The wildcard searches name and description; every
 * column is in the server's sort whitelist. Purpose is the toolbar filter (the API contract's `purpose`), as the Kind
 * filter on the vehicles list. */

const ALL_PURPOSES = "all";

export function EmailTemplatesView() {
  const { t } = useL();
  const [purpose, setPurpose] = useState<string>(ALL_PURPOSES);
  const extra = useMemo(() => ({ purpose: purpose === ALL_PURPOSES ? undefined : (Number(purpose) as EmailTemplatePurpose) }), [purpose]);
  const purposeItems = useMemo(
    () => [{ value: ALL_PURPOSES, label: t("Platform:AllPurposes") }, ...EMAIL_TEMPLATE_PURPOSES.map((p) => ({ value: String(p.value), label: t(p.key) }))],
    [t],
  );
  const fetchRows = emailTemplates.useGridFetcher(extra);
  const deleteOne = emailTemplates.useDelete();
  const bulk = emailTemplates.useBulkDelete();

  const columnDefs = useMemo<ColDef<EmailTemplateRow>[]>(
    () => [
      { field: "name", headerName: t("Platform:Name"), flex: 2, minWidth: 200 },
      {
        field: "purpose",
        headerName: t("Platform:Purpose"),
        valueFormatter: (p) => t(emailTemplatePurposeKey(p.value as EmailTemplatePurpose | undefined)),
        flex: 2,
        minWidth: 180,
      },
      {
        field: "culture",
        headerName: t("Platform:Culture"),
        valueFormatter: (p) => (p.value ? t(cultureKey(p.value as string)) : t("Platform:AllLanguages")),
        flex: 1,
        minWidth: 140,
      },
      { field: "isDefault", headerName: t("Platform:IsDefault"), cellRenderer: BoolCell, flex: 1, minWidth: 100 },
    ],
    [t],
  );

  return (
    <MasterDataListView<EmailTemplateRow>
      entityKey="EmailTemplate"
      resource="Platform"
      permission="Platform.EmailTemplates"
      gridId="platform.emailTemplates"
      columnDefs={columnDefs}
      fetchRows={fetchRows}
      rowName={(row) => row.name ?? ""}
      deleteOne={deleteOne}
      bulkDelete={{ mutateAsync: (selection) => bulk.mutateAsync({ selection, extra }) }}
      filters={
        <div className="w-56">
          <Select items={purposeItems} value={purpose} onValueChange={(next) => setPurpose((next as string | null) ?? ALL_PURPOSES)}>
            <SelectTrigger aria-label={t("Platform:Purpose")} className="h-9 w-full bg-background">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {purposeItems.map((item) => (
                <SelectItem key={item.value} value={item.value}>
                  {item.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      }
      renderEditor={(state, onClose, onSaved) => (
        <EmailTemplateEditor key={`${state.mode}-${state.id ?? "new"}`} mode={state.mode} id={state.id} readOnly={state.readOnly} open onClose={onClose} onSaved={onSaved} />
      )}
    />
  );
}
