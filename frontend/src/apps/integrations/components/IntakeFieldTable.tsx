import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { SOURCE_LOCATION_KINDS, enumLabel, type InboundDocumentIntake } from "@/domains/integrations/order-intake";
import { fieldPathLabel, fieldValueText, flattenIntake } from "@/domains/orders/order-intakes";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The field table of one intake (design 72 "UI design", the trace): every value read from the document with where it
 * came from (a cell, a line with its snippet, an element path, a fixed value, an API field). A small read-only table
 * inside a page section (UI-GRID-001 allows a Table there). Technical source references show in the mono font. */
export function IntakeFieldTable({ intake }: { intake: InboundDocumentIntake }) {
  const { t } = useL();
  const provenance = intake.provenance ?? {};
  const rows = flattenIntake(intake.payload);
  /* A provenance entry for a path without a value (the source was read but empty) still shows. */
  for (const path of Object.keys(provenance)) {
    if (!rows.some((row) => row.path === path)) rows.push({ path, value: null });
  }
  if (rows.length === 0) return <p className="text-sm text-muted-foreground">{t("Integrations:NoFields")}</p>;
  return (
    <Table data-slot="intake-field-table">
      <TableHeader>
        <TableRow>
          <TableHead>{t("Integrations:Field")}</TableHead>
          <TableHead>{t("Integrations:Value")}</TableHead>
          <TableHead>{t("Integrations:SourceLocation")}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map(({ path, value }) => {
          const location = provenance[path];
          return (
            <TableRow key={path} data-path={path}>
              <TableCell className="align-top whitespace-normal">{fieldPathLabel(t, path, intake.payload)}</TableCell>
              <TableCell className="align-top whitespace-normal break-words">{fieldValueText(value)}</TableCell>
              <TableCell className="align-top whitespace-normal">
                {location ? (
                  <div className="grid gap-0.5">
                    <span>
                      {enumLabel(t, "SourceLocationKind", SOURCE_LOCATION_KINDS, location.kind)} <span className="font-mono text-label">{location.ref}</span>
                    </span>
                    {location.snippet && <span className="font-mono text-label break-all text-muted-foreground">{location.snippet}</span>}
                  </div>
                ) : (
                  <span className="text-muted-foreground">{t("Integrations:NoSourceLocation")}</span>
                )}
              </TableCell>
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
}
