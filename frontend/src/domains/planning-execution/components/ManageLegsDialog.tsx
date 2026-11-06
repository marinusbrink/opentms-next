import { useEffect, useMemo, useRef, useState } from "react";
import { Plus, Save, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import type { AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { ReferenceCombobox, type ReferenceOption } from "@/components/ui/reference-combobox";
import { Skeleton } from "@/components/ui/skeleton";
import { Textarea } from "@/components/ui/textarea";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import { AddressField, RelationField } from "@/domains/master-data/components/RelationFields";
import { useWaypointSuccessors, waypointTypes } from "@/domains/orders/reference-data";
import { toOptions } from "@/domains/planning-execution/components/form-fields";
import {
  addLeg,
  canAddLeg,
  canRemoveLeg,
  chainErrors,
  chainRowsOf,
  chainUpdateOf,
  isPlaceLocked,
  removeLeg,
  setAddress,
  setEndField,
  setRelation,
  setRemarks,
  type ChainEnd,
  type ChainRow,
  type ChainSide,
  type SuccessorOf,
} from "@/domains/planning-execution/components/manage-legs-model";
import { useLegChain, useUpdateLegChain, type LegChainDto, type LegDto } from "@/domains/planning-execution/legs";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Manage legs (design 74 §4), from Legs to schedule's row menu ("Add or modify legs"): one block per leg of the order line, in
 * order, under a description of the route. The rules live in manage-legs-model.ts; the server validates the chain on save and
 * its refusal shows in the dialog (UI-FB-004). Legs on a trip or with actuals are read-only with a note saying why; a line whose
 * legs run side by side (`isChain` false) cannot be managed as a route and shows read-only with the reason. */

const REFERENCE_MAX = 128;
const REMARKS_MAX = 2500;

export interface ManageLegsDialogProps {
  leg: LegDto;
  onClose: () => void;
  /** Called after a successful save (the Planboard reloads its list). */
  onDone: () => void;
}

export function ManageLegsDialog({ leg, onClose, onDone }: ManageLegsDialogProps) {
  const { t } = useL();
  const chain = useLegChain(leg.orderLineId);
  const title = `${t("PlanningExecution:ManageLegs")} ${leg.orderNumber ?? ""}`;
  if (!chain.data) {
    return (
      <EntityDialog open onOpenChange={(next) => !next && onClose()} variant="action" size="lg" title={title} footer={<Button type="button" variant="outline" onClick={onClose}>{t("Close")}</Button>}>
        {chain.isError ? (
          <Alert variant="destructive">
            <AlertDescription>{getApiErrorMessage(chain.error, t)}</AlertDescription>
          </Alert>
        ) : (
          <Skeleton className="h-64 w-full" />
        )}
      </EntityDialog>
    );
  }
  return <ManageLegsForm key={leg.orderLineId} chain={chain.data} title={title} onClose={onClose} onDone={onDone} />;
}

function ManageLegsForm({ chain, title, onClose, onDone }: { chain: LegChainDto; title: string; onClose: () => void; onDone: () => void }) {
  const { t } = useL();
  const update = useUpdateLegChain();
  const successorOf = useWaypointSuccessors();
  const lookup = waypointTypes.useLookup();
  const waypointOptions = useMemo(() => toOptions(lookup.data), [lookup.data]);
  const initial = useMemo(() => chainRowsOf(chain), [chain]);
  const [rows, setRows] = useState<ChainRow[]>(initial);
  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const newKeys = useRef(0);
  const busy = update.isPending;
  const isChain = chain.isChain === true;
  const errors = useMemo(() => (submitted ? chainErrors(rows) : new Set<string>()), [submitted, rows]);
  const isDirty = useMemo(() => JSON.stringify(rows) !== JSON.stringify(initial), [rows, initial]);

  /* A failed save may leave the alert out of view at the top of the scrolling section. */
  const alertRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (error) alertRef.current?.scrollIntoView({ block: "nearest" });
  }, [error]);

  const save = async () => {
    setSubmitted(true);
    if (chainErrors(rows).size > 0) return;
    setError(null);
    try {
      await update.mutateAsync({ lineId: chain.orderLineId!, dto: chainUpdateOf(rows, chain) });
      toast.success(t("PlanningExecution:LegsSaved"));
      onClose();
      onDone();
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  const commands: AppCommandBarCommand[] = isChain
    ? [
        { id: "save", labelKey: busy ? "Saving" : "Save", icon: Save, isPrimary: true, disabled: busy, onClick: () => void save() },
        {
          id: "add-leg",
          labelKey: "PlanningExecution:AddLeg",
          icon: Plus,
          disabled: busy || !canAddLeg(rows),
          disabledReasonKey: "PlanningExecution:AddLegRefused",
          onClick: () => {
            newKeys.current += 1;
            setRows((current) => addLeg(current, successorOf, `new-${newKeys.current}`));
          },
        },
      ]
    : [];

  const first = rows[0]?.origin.relation?.name ?? "";
  const last = rows[rows.length - 1]?.destination.relation?.name ?? "";

  return (
    <EntityDialog open onOpenChange={(next) => !next && onClose()} variant="editor" size="lg" title={title} commands={commands} isDirty={isDirty} isBusy={busy}>
      <div className="flex flex-col gap-4" data-slot="manage-legs">
        <p className="text-sm text-muted-foreground" data-slot="manage-legs-route">
          {t("PlanningExecution:ManageLegsRoute").replace("{0}", first).replace("{1}", last)}
        </p>
        {!isChain && (
          <Alert variant="warning">
            <AlertDescription>{t("OpenTms.PlanningExecution:LegLineNotAChain")}</AlertDescription>
          </Alert>
        )}
        {error && (
          <div ref={alertRef}>
            <Alert variant="destructive">
              <AlertDescription>{error}</AlertDescription>
            </Alert>
          </div>
        )}
        {rows.map((row, index) => (
          <LegBlock
            key={row.key}
            rows={rows}
            index={index}
            locked={!isChain || busy}
            errors={errors}
            waypointOptions={waypointOptions}
            successorOf={successorOf}
            onRows={setRows}
          />
        ))}
      </div>
    </EntityDialog>
  );
}

function LegBlock({
  rows,
  index,
  locked,
  errors,
  waypointOptions,
  successorOf,
  onRows,
}: {
  rows: ChainRow[];
  index: number;
  locked: boolean;
  errors: ReadonlySet<string>;
  waypointOptions: ReferenceOption[];
  successorOf: SuccessorOf;
  onRows: (update: (rows: ChainRow[]) => ChainRow[]) => void;
}) {
  const { t } = useL();
  const f = useFormat();
  const row = rows[index];
  const readOnly = locked || row.readOnly;
  const note = row.tripNumber
    ? t("PlanningExecution:LegReadOnlyOnTrip").replace("{0}", row.tripNumber)
    : row.hasActuals
      ? t("PlanningExecution:LegReadOnlyActuals")
      : null;
  const remarksId = `manage-legs-${row.key}-remarks`;

  return (
    <FormSection title={t("PlanningExecution:LegNumber").replace("{0}", String(index + 1))}>
      <div className="flex flex-col gap-3">
        {note && (
          <p className="text-label text-muted-foreground" data-slot="manage-legs-read-only">
            {note}
          </p>
        )}
        <div className="grid grid-cols-2 gap-x-4 gap-y-3">
          <LegEndFields rows={rows} index={index} side="origin" readOnly={readOnly} errors={errors} waypointOptions={waypointOptions} successorOf={successorOf} onRows={onRows} />
          <LegEndFields rows={rows} index={index} side="destination" readOnly={readOnly} errors={errors} waypointOptions={waypointOptions} successorOf={successorOf} onRows={onRows} />
        </div>
        <FormField label={t("PlanningExecution:Remarks")} htmlFor={remarksId} floating="always">
          <Textarea id={remarksId} rows={2} maxLength={REMARKS_MAX} value={row.remarks} disabled={readOnly} onChange={(e) => onRows((current) => setRemarks(current, index, e.target.value))} />
        </FormField>
        <div className="flex items-center justify-between gap-4">
          <dl className="flex gap-4 text-label">
            <div className="flex gap-1">
              <dt className="text-muted-foreground">{`${t("PlanningExecution:Distance")} (km)`}</dt>
              <dd className="tabular-nums">{row.distanceM == null ? "–" : f.number(row.distanceM / 1000, { maxDecimals: 1 })}</dd>
            </div>
            <div className="flex gap-1">
              <dt className="text-muted-foreground">{t("PlanningExecution:Duration")}</dt>
              <dd className="tabular-nums">{row.durationS == null ? "–" : f.duration(row.durationS / 60)}</dd>
            </div>
          </dl>
          {!locked && rows.length > 1 && (
            <Button type="button" variant="ghost" size="sm" disabled={!canRemoveLeg(rows, index)} onClick={() => onRows((current) => removeLeg(current, index))}>
              <Trash2 />
              {t("PlanningExecution:RemoveLeg")}
            </Button>
          )}
        </div>
      </div>
    </FormSection>
  );
}

function LegEndFields({
  rows,
  index,
  side,
  readOnly,
  errors,
  waypointOptions,
  successorOf,
  onRows,
}: {
  rows: ChainRow[];
  index: number;
  side: ChainSide;
  readOnly: boolean;
  errors: ReadonlySet<string>;
  waypointOptions: ReferenceOption[];
  successorOf: SuccessorOf;
  onRows: (update: (rows: ChainRow[]) => ChainRow[]) => void;
}) {
  const { t } = useL();
  const inPlace = useCreateInPlace();
  const row = rows[index];
  const end = row[side];
  const placeLocked = readOnly || isPlaceLocked(rows, index, side);
  const id = (field: string) => `manage-legs-${row.key}-${side}-${field}`;
  const required = t("MasterData:Required");
  const missing = (field: string) => errors.has(`${row.key}.${side}.${field}`);
  const patch = (next: Partial<ChainEnd>) => onRows((current) => setEndField(current, index, side, next));
  const invalidTime = (field: string) => (missing(field) ? t("Orders:InvalidTime") : undefined);

  return (
    <section className="grid grid-cols-2 content-start gap-x-4 gap-y-3" aria-label={t(side === "origin" ? "PlanningExecution:Origin" : "PlanningExecution:Destination")}>
      <h4 className="col-span-2 text-label font-semibold text-brand">{t(side === "origin" ? "PlanningExecution:Origin" : "PlanningExecution:Destination")}</h4>
      <FormField label={t("PlanningExecution:Date")} htmlFor={id("date")} required error={missing("plannedDate") ? required : undefined} floating="always">
        <DatePicker id={id("date")} value={end.plannedDate} onChange={(next) => patch({ plannedDate: next })} required disabled={readOnly} />
      </FormField>
      <FormField label={t("Orders:StopType")} htmlFor={id("activity")} required error={missing("waypointTypeId") ? required : undefined} floating="always">
        <ReferenceCombobox
          id={id("activity")}
          value={end.waypointTypeId}
          onChange={(next) => patch({ waypointTypeId: next })}
          options={waypointOptions}
          required
          disabled={placeLocked}
          aria-invalid={missing("waypointTypeId")}
          {...inPlace.offer("WaypointType", placeLocked, (r) => patch({ waypointTypeId: r.id }))}
        />
      </FormField>
      <RelationField
        id={id("relation")}
        label={t("PlanningExecution:Relation")}
        value={end.relation}
        onChange={(next) => onRows((current) => setRelation(current, index, side, next, successorOf))}
        required
        error={missing("relation") ? required : undefined}
        disabled={placeLocked}
      />
      <AddressField
        id={id("address")}
        label={t("Orders:Address")}
        relationId={end.relation?.id}
        value={end.relationAddressId}
        onChange={(next) => onRows((current) => setAddress(current, index, side, next))}
        disabled={placeLocked}
        preselectDefault={!placeLocked}
      />
      <FormField label={t("PlanningExecution:WindowFrom")} htmlFor={id("from-date")} floating="always">
        <DatePicker id={id("from-date")} value={end.windowFromDate} onChange={(next) => patch({ windowFromDate: next })} disabled={readOnly} />
      </FormField>
      <FormField label={t("Orders:Time")} htmlFor={id("from-time")} error={invalidTime("windowFromTime")} floating="always">
        <Input id={id("from-time")} value={end.windowFromTime} onChange={(e) => patch({ windowFromTime: e.target.value })} placeholder="HH:mm" disabled={readOnly} />
      </FormField>
      <FormField label={t("PlanningExecution:WindowTill")} htmlFor={id("till-date")} floating="always">
        <DatePicker id={id("till-date")} value={end.windowTillDate} onChange={(next) => patch({ windowTillDate: next })} disabled={readOnly} />
      </FormField>
      <FormField label={t("Orders:Time")} htmlFor={id("till-time")} error={invalidTime("windowTillTime")} floating="always">
        <Input id={id("till-time")} value={end.windowTillTime} onChange={(e) => patch({ windowTillTime: e.target.value })} placeholder="HH:mm" disabled={readOnly} />
      </FormField>
      <FormField label={t("PlanningExecution:Reference")} htmlFor={id("reference")} className="col-span-2">
        <Input id={id("reference")} maxLength={REFERENCE_MAX} value={end.reference} onChange={(e) => patch({ reference: e.target.value })} disabled={readOnly} />
      </FormField>
      {inPlace.dialog}
    </section>
  );
}
