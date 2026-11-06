import { useMemo, useState, type FormEvent } from "react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { useTransportOrder } from "@/domains/orders/transport-orders";
import { complementOf, isValidSplit, splitQuantities, type LineMeasures } from "@/domains/planning-execution/components/split-line-model";
import { legEndLabel, useSplitOrderLine, type LegDto } from "@/domains/planning-execution/legs";
import { getApiErrorMessage } from "@/lib/api/errors";
import { parseNumber } from "@/lib/forms/use-form-state";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Split line, as legacy (design 74 §7), from Legs to schedule's row menu: read-only unit, product, origin and destination, the
 * total, and two columns "Remaining" and "New line", each with an editable amount and read-only measures that recalculate
 * live. OK needs both amounts above zero; the server decides the stored quantities. The order's line gives the base; until it
 * has loaded (or without the right to read orders) the leg's own measures do, which carry everything except the colli. */

const measuresOfLeg = (leg: LegDto): LineMeasures => ({
  quantity: leg.quantity ?? 0,
  colli: null,
  weightKg: leg.weightKg ?? null,
  volumeM3: leg.volumeM3 ?? null,
  loadingMeters: leg.loadingMeters ?? null,
  loadFactor: leg.loadFactor ?? null,
  epq: leg.epq ?? null,
});

export interface SplitLineDialogProps {
  leg: LegDto;
  onClose: () => void;
  onDone: () => void;
}

export function SplitLineDialog({ leg, onClose, onDone }: SplitLineDialogProps) {
  const { t } = useL();
  const f = useFormat();
  const split = useSplitOrderLine();
  const order = useTransportOrder(leg.orderId);
  const line = order.data?.lines?.find((l) => l.id?.toLowerCase() === leg.orderLineId?.toLowerCase());
  const total = useMemo<LineMeasures>(
    () =>
      line
        ? { quantity: line.quantity ?? 0, colli: line.colli ?? null, weightKg: line.weightKg ?? null, volumeM3: line.volumeM3 ?? null, loadingMeters: line.loadingMeters ?? null, loadFactor: line.loadFactor ?? null, epq: line.epq ?? null }
        : measuresOfLeg(leg),
    [line, leg],
  );
  const [newText, setNewText] = useState("");
  const [remainingText, setRemainingText] = useState("");
  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const busy = split.isPending;

  /* What the server gets: exactly what was typed or its exact complement, not the two decimals the other column shows. */
  const [amount, setAmount] = useState<number | null>(null);
  const valid = amount != null && isValidSplit(total.quantity, amount);
  const result = splitQuantities(total, valid ? amount : null);
  const amountText = (value: number | null) => (value == null ? "" : String(Math.round(value * 100) / 100));

  const onNew = (text: string) => {
    setAmount(parseNumber(text));
    setNewText(text);
    setRemainingText(amountText(complementOf(total.quantity, parseNumber(text))));
  };
  const onRemaining = (text: string) => {
    setRemainingText(text);
    const complement = complementOf(total.quantity, parseNumber(text));
    setAmount(complement);
    setNewText(amountText(complement));
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setSubmitted(true);
    if (!valid || amount == null) return;
    setError(null);
    try {
      await split.mutateAsync({ legId: leg.id!, quantity: amount });
      toast.success(t("PlanningExecution:LineSplit"));
      onClose();
      onDone();
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  const rangeError = submitted && !valid ? t("PlanningExecution:SplitLineAmounts") : undefined;
  const measure = (value: number | null | undefined) => f.number(value, { maxDecimals: 2 });
  const rows: { key: string; of: (m: LineMeasures) => number | null }[] = [
    { key: "Orders:WeightKg", of: (m) => m.weightKg },
    { key: "Orders:Colli", of: (m) => m.colli },
    { key: "Orders:VolumeM3", of: (m) => m.volumeM3 },
    { key: "Orders:LoadingMeters", of: (m) => m.loadingMeters },
    { key: "Orders:LoadFactor", of: (m) => m.loadFactor },
    { key: "Orders:Epq", of: (m) => m.epq },
  ];

  const column = (id: string, labelKey: string, text: string, onChange: (text: string) => void, measures: LineMeasures | undefined) => (
    <div className="flex flex-col gap-3" data-slot={`split-line-${id}`}>
      <FormField label={t(labelKey)} htmlFor={`split-line-${id}`} required error={rangeError} floating="always">
        <Input id={`split-line-${id}`} inputMode="decimal" className="tabular-nums" value={text} onChange={(e) => onChange(e.target.value)} disabled={busy} autoFocus={id === "new"} />
      </FormField>
      <dl className="grid grid-cols-2 gap-x-3 gap-y-1">
        {rows.map((row) => (
          <div key={row.key} className="contents">
            <dt className="text-label text-muted-foreground">{t(row.key)}</dt>
            <dd className="text-right text-sm tabular-nums">{measures ? measure(row.of(measures)) : "–"}</dd>
          </div>
        ))}
      </dl>
    </div>
  );

  return (
    <EntityDialog
      open
      onOpenChange={(next) => !next && onClose()}
      variant="action"
      size="sm"
      title={`${t("PlanningExecution:SplitLine")} ${leg.orderNumber ?? ""}`}
      isBusy={busy}
      footer={
        <>
          <Button type="button" variant="outline" onClick={onClose} disabled={busy}>
            {t("Cancel")}
          </Button>
          <Button type="submit" form="split-line-form" disabled={busy}>
            {busy ? t("Saving") : t("PlanningExecution:Split")}
          </Button>
        </>
      }
    >
      <form id="split-line-form" onSubmit={(e) => void submit(e)} noValidate className="grid grid-cols-1 gap-3">
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <dl className="grid grid-cols-2 gap-x-3 gap-y-1">
          <dt className="text-label text-muted-foreground">{t("Orders:TransportUnit")}</dt>
          <dd className="text-sm">{leg.transportUnitName ?? ""}</dd>
          <dt className="text-label text-muted-foreground">{t("Orders:Product")}</dt>
          <dd className="text-sm">{leg.productName ?? ""}</dd>
          <dt className="text-label text-muted-foreground">{t("PlanningExecution:Origin")}</dt>
          <dd className="text-sm">{legEndLabel(leg.origin)}</dd>
          <dt className="text-label text-muted-foreground">{t("PlanningExecution:Destination")}</dt>
          <dd className="text-sm">{legEndLabel(leg.destination)}</dd>
        </dl>
        <p className="text-label text-muted-foreground" data-slot="split-line-hint">
          {t("PlanningExecution:SplitLineHint").replace("{0}", measure(total.quantity))}
        </p>
        <div className="grid grid-cols-2 gap-x-4">
          {column("remaining", "PlanningExecution:SplitLineRemaining", remainingText, onRemaining, result?.remaining)}
          {column("new", "PlanningExecution:SplitLineNew", newText, onNew, result?.newLine)}
        </div>
      </form>
    </EntityDialog>
  );
}
