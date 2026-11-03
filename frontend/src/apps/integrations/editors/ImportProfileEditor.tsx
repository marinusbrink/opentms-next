import { useCallback, useState } from "react";
import { Archive, CheckCheck } from "lucide-react";
import { toast } from "sonner";

import { useAppFeatures } from "@/app/app-features";
import type { RibbonCommand } from "@/components/ui/entity-dialog";
import { RecordLink } from "@/components/ui/record-link";
import { StatusBadge } from "@/components/ui/status-badge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { EntityEditorProps } from "@/components/record-editor/editor-props";
import { TextField } from "@/components/record-editor/fields";
import { MasterDataEditor, type FieldErrors, type FormContext } from "@/components/record-editor/MasterDataEditor";
import {
  DOCUMENT_FORMATS,
  FIELD_SOURCE_KINDS,
  FIELD_TRANSFORM_KINDS,
  IMPORT_PROFILE_STATUSES,
  PROFILE_ORIGINS,
  enumLabel,
  enumName,
  useActivateImportProfile,
  useDeleteImportProfile,
  useImportProfile,
  useRenameImportProfile,
  useRetireImportProfile,
  type FieldRule,
  type FieldTransform,
  type ImportProfileDto,
  type ImportProfileRules,
} from "@/domains/integrations/order-intake";
import { INTAKE_STOP_ROLES } from "@/domains/orders/order-intakes";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* An import profile (design 72 "Import profiles", "UI design"): the rules are read-only (they are corrected through the
 * review, never edited here), shown as a table field ← source → transforms with the structure and the signature; the
 * general tab holds the name (rename), what the profile is (sender, format, version, origin, model), its counters, and a
 * link to the trace of the document it was learned from. Activate and Retire are ribbon commands, Delete is last.
 * Read-only without Integrations.ImportProfiles.Manage. */

interface ProfileValues {
  name: string;
}

const validate = (v: ProfileValues): FieldErrors<ProfileValues> => (v.name.trim() ? {} : { name: "MasterData:Required" });

export interface ImportProfileEditorProps extends Omit<EntityEditorProps, "mode" | "initialName"> {
  id: string;
}

export function ImportProfileEditor({ id, readOnly, open, onClose, onSaved }: ImportProfileEditorProps) {
  const { t } = useL();
  const f = useFormat();
  const features = useAppFeatures();
  const canManage = useIsGranted("Integrations.ImportProfiles.Manage");
  const canTrace = useIsGranted("Integrations.InboundMessages") && features.isFeatureEnabled("Apps.Integrations.Intake.Received");
  const detail = useImportProfile(id);
  const rename = useRenameImportProfile();
  const activate = useActivateImportProfile();
  const retire = useRetireImportProfile();
  const remove = useDeleteImportProfile();
  const [commandError, setCommandError] = useState<string | null>(null);

  const initialValues = useCallback((dto: ImportProfileDto | undefined): ProfileValues => ({ name: dto?.name ?? "" }), []);
  const save = (values: ProfileValues, dto: ImportProfileDto | undefined) => rename.mutateAsync({ id, name: values.name.trim(), concurrencyStamp: dto?.concurrencyStamp ?? "" });

  const run = async (action: () => Promise<unknown>, successKey: string) => {
    setCommandError(null);
    try {
      await action();
      toast.success(t(successKey));
      onSaved();
    } catch (err) {
      setCommandError(getApiErrorMessage(err, t));
    }
  };

  const dto = detail.data;
  const status = enumName(IMPORT_PROFILE_STATUSES, dto?.status);
  const actionCommands: RibbonCommand[] =
    canManage && dto
      ? [
          ...(status !== "Active" ? [{ id: "activate", labelKey: "Integrations:Activate", icon: CheckCheck, groupKey: "Integrations:ProfileState", onClick: () => void run(() => activate.mutateAsync(id), "Integrations:ImportProfileActivated") }] : []),
          ...(status !== "Retired" ? [{ id: "retire", labelKey: "Integrations:Retire", icon: Archive, groupKey: "Integrations:ProfileState", onClick: () => void run(() => retire.mutateAsync(id), "Integrations:ImportProfileRetired") }] : []),
        ]
      : [];

  const row = (labelKey: string, value: React.ReactNode) => (
    <>
      <dt className="text-label font-medium text-muted-foreground">{t(labelKey)}</dt>
      <dd className="min-w-0 break-words">{value}</dd>
    </>
  );

  const renderGeneral = (form: FormContext<ProfileValues>) => (
    <div className="grid grid-cols-1 gap-4">
      <TextField form={form} field="name" labelKey="Integrations:ImportProfileName" required maxLength={128} />
      {dto && (
        <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 rounded-lg border border-border-section bg-surface-section p-4 text-sm">
          {row("Integrations:Sender", dto.senderKey || t("Integrations:AnySender"))}
          {row("Integrations:Format", enumLabel(t, "DocumentFormat", DOCUMENT_FORMATS, dto.format))}
          {row("Integrations:Version", <span className="tabular-nums">{f.number(dto.version ?? 0, { decimals: 0 })}</span>)}
          {row("Integrations:Origin", enumLabel(t, "ProfileOrigin", PROFILE_ORIGINS, dto.origin))}
          {dto.modelId && row("Integrations:Model", <span className="font-mono text-label">{dto.modelId}</span>)}
          {row("Integrations:ConfirmedCount", <span className="tabular-nums">{f.number(dto.confirmedCount ?? 0, { decimals: 0 })}</span>)}
          {row("Integrations:CorrectedCount", <span className="tabular-nums">{f.number(dto.correctedCount ?? 0, { decimals: 0 })}</span>)}
          {row("Integrations:LastUsedAt", <span className="tabular-nums">{dto.lastUsedAt ? f.dateTime(dto.lastUsedAt) : t("Integrations:NeverUsed")}</span>)}
          {row(
            "Integrations:LearnedFrom",
            dto.sourceInboundMessageId && canTrace ? (
              <RecordLink to={`/integrations/received/${dto.sourceInboundMessageId}`}>{t("Integrations:ViewTrace")}</RecordLink>
            ) : (
              <span className="text-muted-foreground">{t("Integrations:TraceUnavailable")}</span>
            ),
          )}
        </dl>
      )}
      <p className="text-label text-muted-foreground">{t("Integrations:ImportProfileReadOnlyNotice")}</p>
    </div>
  );

  const renderRules = () => (dto?.rules ? <RulesView rules={dto.rules} signature={dto.signature ?? []} /> : null);

  return (
    <MasterDataEditor<ProfileValues, ImportProfileDto>
      entityKey="ImportProfile"
      resource="Integrations"
      queryEntity="integrations.importProfiles"
      size="md"
      mode="edit"
      readOnly={readOnly}
      open={open}
      onClose={onClose}
      onSaved={onSaved}
      detail={detail}
      titleOf={(d) => `${d.name ?? ""} (${t("Integrations:VersionN").replace("{0}", String(d.version ?? ""))})`}
      initialValues={initialValues}
      validate={validate}
      save={save}
      isSaving={rename.isPending}
      badges={status ? <StatusBadge entity="ImportProfile" state={status} /> : undefined}
      actionCommands={actionCommands}
      isCommandBusy={activate.isPending || retire.isPending}
      commandError={commandError}
      deleteRecord={canManage ? remove : undefined}
      tabs={[
        { value: "general", labelKey: "General", fields: ["name"], render: renderGeneral },
        { value: "rules", labelKey: "Integrations:Rules", fields: [], render: renderRules },
      ]}
    />
  );
}

/** One source of a rule: `column "Ordernr"`, `cell B7`, `regex … (group 1)`. */
function sourceText(t: (key: string) => string, rule: FieldRule): string {
  const source = rule.source;
  if (!source) return "";
  const kind = enumName(FIELD_SOURCE_KINDS, source.kind) ?? String(source.kind);
  const extras = [
    source.group != null ? t("Integrations:RuleGroup").replace("{0}", String(source.group)) : null,
    source.occurrence != null ? t("Integrations:RuleOccurrence").replace("{0}", String(source.occurrence)) : null,
  ].filter(Boolean);
  return `${kind} ${source.value ?? ""}${extras.length ? ` (${extras.join(", ")})` : ""}`.trim();
}

/** One transform: `date dd-MM-yyyy nl-NL`, `map EUR-pallet → Europallet`, `split " " 0`. */
function transformText(transform: FieldTransform): string {
  const kind = enumName(FIELD_TRANSFORM_KINDS, transform.kind) ?? String(transform.kind);
  const map = transform.map ? Object.entries(transform.map).map(([from, to]) => `${from} → ${to}`).join(", ") : null;
  const separator = transform.separator != null ? JSON.stringify(transform.separator) : null;
  return [kind, transform.format, transform.culture, map, separator, transform.index != null ? String(transform.index) : null].filter(Boolean).join(" ");
}

/* The read-only rules: the structure (how a document splits into orders, lines and stops), the field table and the
 * signature that must all be present. Technical content, so the DSL values show in the mono font (UI-TOK-008). */
function RulesView({ rules, signature }: { rules: ImportProfileRules; signature: string[] }) {
  const { t } = useL();
  const scope = (kind: string | undefined, value: string | null | undefined) => [kind, value].filter(Boolean).join(" ");
  const orderKinds: Record<number, string> = { 1: "document", 2: "rowGroup", 3: "xpath", 4: "textSplit" };
  const lineKinds: Record<number, string> = { 1: "single", 2: "row", 3: "xpath", 4: "textBlock" };
  const stopKinds: Record<number, string> = { 1: "inherit", 2: "xpath" };
  return (
    <div className="grid grid-cols-1 gap-4" data-slot="import-profile-rules">
      <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 rounded-lg border border-border-section bg-surface-section p-4 text-sm">
        <dt className="text-label font-medium text-muted-foreground">{t("Integrations:RuleOrders")}</dt>
        <dd className="font-mono text-label break-all">
          {scope(orderKinds[rules.orders?.scope?.kind ?? 0], rules.orders?.scope?.value)}
          {rules.orders?.scope?.groupBy?.length ? ` [${rules.orders.scope.groupBy.join(", ")}]` : ""}
        </dd>
        <dt className="text-label font-medium text-muted-foreground">{t("Integrations:RuleLines")}</dt>
        <dd className="font-mono text-label break-all">{scope(lineKinds[rules.lines?.scope?.kind ?? 0], rules.lines?.scope?.value)}</dd>
        {(rules.stops ?? []).map((stop, index) => (
          <div key={index} className="contents">
            <dt className="text-label font-medium text-muted-foreground">{stop.role ? t(`Orders:IntakeStopRole.${INTAKE_STOP_ROLES[stop.role]}`) : t("Integrations:RuleStop")}</dt>
            <dd className="font-mono text-label break-all">{scope(stopKinds[stop.scope?.kind ?? 0], stop.scope?.value)}</dd>
          </div>
        ))}
        {rules.sheet && (
          <>
            <dt className="text-label font-medium text-muted-foreground">{t("Integrations:RuleSheet")}</dt>
            <dd className="font-mono text-label">{rules.sheet}</dd>
          </>
        )}
        {rules.headerRow != null && (
          <>
            <dt className="text-label font-medium text-muted-foreground">{t("Integrations:RuleHeaderRow")}</dt>
            <dd className="tabular-nums">{rules.headerRow}</dd>
          </>
        )}
      </dl>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{t("Integrations:RuleField")}</TableHead>
            <TableHead>{t("Integrations:RuleSource")}</TableHead>
            <TableHead>{t("Integrations:RuleTransforms")}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {(rules.fields ?? []).map((rule, index) => (
            <TableRow key={`${rule.target}-${index}`}>
              <TableCell className="font-mono text-label whitespace-normal break-all">{rule.target}</TableCell>
              <TableCell className="font-mono text-label whitespace-normal break-all">← {sourceText(t, rule)}</TableCell>
              <TableCell className="font-mono text-label whitespace-normal break-all">{(rule.transforms ?? []).length ? `→ ${(rule.transforms ?? []).map(transformText).join(" → ")}` : ""}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      <div className="grid gap-1">
        <span className="text-label font-semibold text-brand">{t("Integrations:Signature")}</span>
        <p className="text-label text-muted-foreground">{t("Integrations:Signature.Hint")}</p>
        <ul className="flex flex-wrap gap-2">
          {signature.map((item) => (
            <li key={item} className="rounded-md border border-border-section bg-surface-section px-2 py-0.5 font-mono text-label">
              {item}
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}
