import { useState, type FormEvent } from "react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { windowError, windowInput, type WindowTexts } from "@/apps/transport/components/leg-window";
import { useUpdateLeg, type LegDto, type LegEndSnapshotDto } from "@/domains/planning-execution/legs";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The editable fields of a leg (design 59, OpenTMS's leg grid), shared by the order's Planning tab and the trip's Legs
 * tab: per end the reference, the planned date and the planned time window (from / till, HH:mm on that date; from
 * not after till), and the remarks. A window is sent only when changed (see leg-window.ts); the server checks the
 * planned dates against the resulting window (`InvalidLegWindow`, `LegOutsideTripWindow`) and the refusal shows here. */

export interface LegDialogProps {
  leg: LegDto;
  title: string;
  /** Prefix of the field ids (two tabs can hold a leg dialog). */
  idPrefix: string;
  onClose: () => void;
}

export function LegDialog({ leg, title, idPrefix, onClose }: LegDialogProps) {
  const { t } = useL();
  const f = useFormat();
  const update = useUpdateLeg();
  const textsOf = (end: LegEndSnapshotDto | undefined): WindowTexts => ({ from: f.time(end?.windowFrom), till: f.time(end?.windowTill) });
  const [initial] = useState(() => ({ origin: textsOf(leg.origin), destination: textsOf(leg.destination) }));
  const [originReference, setOriginReference] = useState(leg.origin?.reference ?? "");
  const [destinationReference, setDestinationReference] = useState(leg.destination?.reference ?? "");
  const [originDate, setOriginDate] = useState<string | null>(leg.origin?.plannedDate ?? null);
  const [destinationDate, setDestinationDate] = useState<string | null>(leg.destination?.plannedDate ?? null);
  const [originWindow, setOriginWindow] = useState<WindowTexts>(initial.origin);
  const [destinationWindow, setDestinationWindow] = useState<WindowTexts>(initial.destination);
  const [remarks, setRemarks] = useState(leg.remarks ?? "");
  const [error, setError] = useState<string | null>(null);
  const [submitted, setSubmitted] = useState(false);
  const originError = submitted ? windowError(originWindow) : undefined;
  const destinationError = submitted ? windowError(destinationWindow) : undefined;
  const id = (field: string) => `${idPrefix}-${field}`;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setSubmitted(true);
    if (windowError(originWindow) || windowError(destinationWindow)) return;
    setError(null);
    try {
      await update.mutateAsync({
        id: leg.id!,
        dto: {
          concurrencyStamp: leg.concurrencyStamp ?? "",
          originReference: originReference.trim() || null,
          destinationReference: destinationReference.trim() || null,
          originPlannedDate: originDate,
          destinationPlannedDate: destinationDate,
          originWindow: windowInput(initial.origin, originWindow, leg.origin?.plannedDate ?? null, originDate),
          destinationWindow: windowInput(initial.destination, destinationWindow, leg.destination?.plannedDate ?? null, destinationDate),
          remarks: remarks.trim() || null,
        },
      });
      toast.success(t("PlanningExecution:LegSaved"));
      onClose();
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  const end = (
    side: "origin" | "destination",
    reference: string,
    setReference: (v: string) => void,
    date: string | null,
    setDate: (v: string | null) => void,
    window: WindowTexts,
    setWindow: (v: WindowTexts) => void,
    windowErrorKey: string | undefined,
  ) => {
    const sideLabel = t(side === "origin" ? "PlanningExecution:Origin" : "PlanningExecution:Destination");
    return (
      <fieldset className="grid grid-cols-4 gap-x-4 gap-y-3" data-slot={`leg-${side}`}>
        <legend className="col-span-4 mb-1 text-label font-medium text-muted-foreground">{sideLabel}</legend>
        <FormField label={`${t("PlanningExecution:Reference")} (${sideLabel})`} htmlFor={id(`${side}-reference`)} className="col-span-4">
          <Input id={id(`${side}-reference`)} value={reference} maxLength={128} onChange={(e) => setReference(e.target.value)} autoFocus={side === "origin"} />
        </FormField>
        <FormField label={`${t("PlanningExecution:PlannedDate")} (${sideLabel})`} htmlFor={id(`${side}-date`)} floating="always" className="col-span-2">
          <DatePicker id={id(`${side}-date`)} value={date} onChange={setDate} />
        </FormField>
        <FormField label={`${t("Orders:WindowFrom")} (${sideLabel})`} htmlFor={id(`${side}-window-from`)} error={windowErrorKey ? t(windowErrorKey) : undefined}>
          <Input id={id(`${side}-window-from`)} className="tabular-nums" value={window.from} placeholder="HH:mm" aria-invalid={windowErrorKey != null} onChange={(e) => setWindow({ ...window, from: e.target.value })} />
        </FormField>
        <FormField label={`${t("Orders:WindowTill")} (${sideLabel})`} htmlFor={id(`${side}-window-till`)}>
          <Input id={id(`${side}-window-till`)} className="tabular-nums" value={window.till} placeholder="HH:mm" aria-invalid={windowErrorKey != null} onChange={(e) => setWindow({ ...window, till: e.target.value })} />
        </FormField>
      </fieldset>
    );
  };

  return (
    <EntityDialog
      open
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="action"
      size="md"
      title={title}
      isBusy={update.isPending}
      footer={
        <>
          <Button type="button" variant="outline" onClick={onClose} disabled={update.isPending}>
            {t("Cancel")}
          </Button>
          <Button type="submit" form={id("form")} disabled={update.isPending}>
            {update.isPending ? t("Saving") : t("Save")}
          </Button>
        </>
      }
    >
      <form id={id("form")} onSubmit={(e) => void submit(e)} noValidate className="grid grid-cols-1 gap-4">
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        {end("origin", originReference, setOriginReference, originDate, setOriginDate, originWindow, setOriginWindow, originError)}
        {end("destination", destinationReference, setDestinationReference, destinationDate, setDestinationDate, destinationWindow, setDestinationWindow, destinationError)}
        <FormField label={t("PlanningExecution:Remarks")} htmlFor={id("remarks")} floating="always">
          <Textarea id={id("remarks")} rows={3} value={remarks} maxLength={2500} onChange={(e) => setRemarks(e.target.value)} />
        </FormField>
      </form>
    </EntityDialog>
  );
}
