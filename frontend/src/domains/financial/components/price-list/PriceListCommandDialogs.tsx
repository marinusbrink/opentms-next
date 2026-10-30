import { useMemo, useRef, useState, type FormEvent } from "react";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { parseNumber } from "@/components/record-editor/fields";
import {
  MAX_IMPORT_BYTES,
  PRICE_ROUNDINGS,
  useAdjustPriceList,
  useCopyPriceList,
  useImportPriceList,
  type PriceListAdjustResultDto,
  type PriceListDto,
  type PriceListImportResultDto,
  type PriceRounding,
} from "@/domains/financial/price-lists";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The three action dialogs of the price list editor's ribbon (design 57 §UI; UI-DLG-002/005/013, UI-ACT-003): Copy
 * (a new code and name, its own validity), Adjust (a percentage or an amount, rounded, with a preview of the cells
 * that would change before the change is applied) and Import (an xlsx that replaces the whole rule set). Export needs
 * no dialog: the file downloads at once. */

const XLSX_TYPE = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

// ── Copy ──────────────────────────────────────────────────────────────────────

export interface CopyPriceListDialogProps {
  open: boolean;
  onClose: () => void;
  source: PriceListDto;
  onCopied: (copy: PriceListDto) => void;
}

export function CopyPriceListDialog({ open, onClose, source, onCopied }: CopyPriceListDialogProps) {
  const { t } = useL();
  const copy = useCopyPriceList();
  const formRef = useRef<HTMLFormElement>(null);
  const [code, setCode] = useState("");
  const [name, setName] = useState(() => `${source.name ?? ""} (${t("Financial:Copy").toLowerCase()})`);
  const [validFrom, setValidFrom] = useState<string | null>(source.validFrom ?? null);
  const [validTill, setValidTill] = useState<string | null>(source.validTill ?? null);
  const [submitted, setSubmitted] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);

  const errors = {
    code: code.trim() ? undefined : t("MasterData:Required"),
    name: name.trim() ? undefined : t("MasterData:Required"),
    validFrom: validFrom ? undefined : t("MasterData:Required"),
    validTill: validTill && validFrom && validTill < validFrom ? t("Financial:ValidTillBeforeFromHint") : undefined,
  };
  const shown = (field: keyof typeof errors) => (submitted ? errors[field] : undefined);

  const submit = async (e?: FormEvent) => {
    e?.preventDefault();
    if (copy.isPending || !source.id) return;
    setSubmitted(true);
    setServerError(null);
    if (Object.values(errors).some(Boolean)) return;
    try {
      const created = await copy.mutateAsync({ id: source.id, dto: { code: code.trim().toUpperCase(), name: name.trim(), validFrom: validFrom!, validTill } });
      onCopied(created);
    } catch (err) {
      setServerError(getApiErrorMessage(err, t));
    }
  };

  return (
    <EntityDialog
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="action"
      size="sm"
      title={`${t("Financial:CopyPriceList")} ${source.code ?? ""}`}
      isBusy={copy.isPending}
      footer={
        <>
          <Button type="button" variant="outline" onClick={onClose} disabled={copy.isPending}>
            {t("Cancel")}
          </Button>
          <Button type="submit" form="copy-price-list-form" disabled={copy.isPending}>
            {copy.isPending ? t("Financial:Copying") : t("Financial:Copy")}
          </Button>
        </>
      }
    >
      <form id="copy-price-list-form" ref={formRef} onSubmit={(e) => void submit(e)} className="grid grid-cols-2 gap-x-4 gap-y-3" noValidate>
        {serverError && (
          <Alert variant="destructive" className="col-span-2">
            <AlertDescription>{serverError}</AlertDescription>
          </Alert>
        )}
        <p className="col-span-2 text-label text-muted-foreground">{t("Financial:CopyPriceListHint")}</p>
        <FormField label={t("MasterData:Code")} htmlFor="copy-code" required error={shown("code")}>
          <Input id="copy-code" value={code} maxLength={16} onChange={(e) => setCode(e.target.value.toUpperCase())} />
        </FormField>
        <FormField label={t("MasterData:Name")} htmlFor="copy-name" required error={shown("name")}>
          <Input id="copy-name" value={name} maxLength={128} onChange={(e) => setName(e.target.value)} />
        </FormField>
        <FormField label={t("Financial:ValidFrom")} htmlFor="copy-valid-from" required error={shown("validFrom")} floating="always">
          <DatePicker id="copy-valid-from" value={validFrom} onChange={setValidFrom} required />
        </FormField>
        <FormField label={t("Financial:ValidTill")} htmlFor="copy-valid-till" error={shown("validTill")} floating="always">
          <DatePicker id="copy-valid-till" value={validTill} onChange={setValidTill} />
        </FormField>
      </form>
    </EntityDialog>
  );
}

// ── Adjust ────────────────────────────────────────────────────────────────────

export interface AdjustPriceListDialogProps {
  open: boolean;
  onClose: () => void;
  priceListId: string;
  currencyCode: string;
  onAdjusted: (result: PriceListAdjustResultDto) => void;
}

type AdjustKind = "percentage" | "amount";

export function AdjustPriceListDialog({ open, onClose, priceListId, currencyCode, onAdjusted }: AdjustPriceListDialogProps) {
  const { t } = useL();
  const f = useFormat();
  const adjust = useAdjustPriceList();
  const [kind, setKind] = useState<AdjustKind>("percentage");
  const [value, setValue] = useState("");
  const [rounding, setRounding] = useState<PriceRounding>(0);
  const [submitted, setSubmitted] = useState(false);
  const [preview, setPreview] = useState<PriceListAdjustResultDto | null>(null);
  const [serverError, setServerError] = useState<string | null>(null);

  const kindItems = useMemo(
    () => [
      { value: "percentage" as AdjustKind, label: t("Financial:AdjustByPercentage") },
      { value: "amount" as AdjustKind, label: t("Financial:AdjustByAmount") },
    ],
    [t],
  );
  const roundingItems = useMemo(() => PRICE_ROUNDINGS.map((r) => ({ value: r.value, label: t(r.key) })), [t]);

  const number = parseNumber(value);
  const valueError = number == null || Number.isNaN(number) ? t("MasterData:InvalidNumber") : kind === "percentage" && number <= -100 ? t("Financial:AdjustPercentageRangeHint") : undefined;

  const run = async (isPreview: boolean) => {
    setSubmitted(true);
    setServerError(null);
    if (valueError) return;
    try {
      const dto = { percentage: kind === "percentage" ? number : null, amount: kind === "amount" ? number : null, rounding, preview: isPreview };
      const result = await adjust.mutateAsync({ id: priceListId, dto });
      if (isPreview) {
        setPreview(result);
      } else {
        onAdjusted(result);
      }
    } catch (err) {
      setServerError(getApiErrorMessage(err, t));
    }
  };

  return (
    <EntityDialog
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="action"
      size="md"
      title={t("Financial:AdjustPriceList")}
      isBusy={adjust.isPending}
      footer={
        <>
          <Button type="button" variant="outline" onClick={onClose} disabled={adjust.isPending}>
            {t("Cancel")}
          </Button>
          <Button type="button" variant="outline" onClick={() => void run(true)} disabled={adjust.isPending}>
            {t("Financial:Preview")}
          </Button>
          <Button type="button" onClick={() => void run(false)} disabled={adjust.isPending || preview == null}>
            {adjust.isPending ? t("Financial:Adjusting") : t("Financial:ApplyAdjustment")}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        {serverError && (
          <Alert variant="destructive">
            <AlertDescription>{serverError}</AlertDescription>
          </Alert>
        )}
        <Alert variant="warning">
          <AlertDescription>{t("Financial:AdjustWarning")}</AlertDescription>
        </Alert>
        <div className="grid grid-cols-3 gap-x-4 gap-y-3">
          <FormField label={t("Financial:Adjustment")} htmlFor="adjust-kind" required floating="always">
            <Select
              items={kindItems}
              value={kind}
              onValueChange={(next) => {
                setKind((next as AdjustKind | null) ?? "percentage");
                setPreview(null);
              }}
            >
              <SelectTrigger id="adjust-kind" className="h-10 w-full bg-background">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {kindItems.map((item) => (
                  <SelectItem key={item.value} value={item.value}>
                    {item.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </FormField>
          <FormField label={kind === "percentage" ? `${t("Financial:Percentage")} (%)` : `${t("Financial:Amount")} (${currencyCode})`} htmlFor="adjust-value" required error={submitted ? valueError : undefined}>
            <Input
              id="adjust-value"
              inputMode="decimal"
              value={value}
              onChange={(e) => {
                setValue(e.target.value);
                setPreview(null);
              }}
            />
          </FormField>
          <FormField label={t("Financial:Rounding")} htmlFor="adjust-rounding" required floating="always">
            <Select
              items={roundingItems}
              value={rounding}
              onValueChange={(next) => {
                setRounding((next as PriceRounding | null) ?? 0);
                setPreview(null);
              }}
            >
              <SelectTrigger id="adjust-rounding" className="h-10 w-full bg-background">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {roundingItems.map((item) => (
                  <SelectItem key={item.value} value={item.value}>
                    {item.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </FormField>
        </div>
        {preview && (
          <div className="flex flex-col gap-2" data-slot="adjust-preview">
            <p className="text-sm">
              <span className="text-label text-muted-foreground">{t("Financial:ChangedCells")}: </span>
              <span className="font-semibold tabular-nums">{f.number(preview.changedCellCount)}</span>
            </p>
            {(preview.sample ?? []).length > 0 && (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t("Financial:Rule")}</TableHead>
                    <TableHead>{t("Financial:Bracket")}</TableHead>
                    <TableHead>{t("Financial:Column")}</TableHead>
                    <TableHead className="text-right">{t("Financial:Before")}</TableHead>
                    <TableHead className="text-right">{t("Financial:After")}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {(preview.sample ?? []).map((s, index) => (
                    <TableRow key={`${s.ruleId}-${s.bracketPosition}-${s.columnPosition}-${index}`}>
                      <TableCell className="font-mono text-label">{(s.ruleId ?? "").slice(0, 8)}</TableCell>
                      <TableCell className="tabular-nums">{s.bracketPosition}</TableCell>
                      <TableCell className="tabular-nums">{s.columnPosition}</TableCell>
                      <TableCell className="text-right tabular-nums">{f.unitPrice(s.before)}</TableCell>
                      <TableCell className="text-right tabular-nums">{f.unitPrice(s.after)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </div>
        )}
      </div>
    </EntityDialog>
  );
}

// ── Import ────────────────────────────────────────────────────────────────────

export interface ImportPriceListDialogProps {
  open: boolean;
  onClose: () => void;
  priceListId: string;
  onImported: (result: PriceListImportResultDto) => void;
}

export function ImportPriceListDialog({ open, onClose, priceListId, onImported }: ImportPriceListDialogProps) {
  const { t } = useL();
  const f = useFormat();
  const importList = useImportPriceList();
  const [file, setFile] = useState<File | null>(null);
  const [submitted, setSubmitted] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);

  const fileError = !file ? t("MasterData:Required") : file.size > MAX_IMPORT_BYTES ? t("Financial:ImportFileTooLargeHint") : undefined;

  const submit = async (e?: FormEvent) => {
    e?.preventDefault();
    if (importList.isPending) return;
    setSubmitted(true);
    setServerError(null);
    if (fileError || !file) return;
    try {
      onImported(await importList.mutateAsync({ id: priceListId, file }));
    } catch (err) {
      setServerError(getApiErrorMessage(err, t));
    }
  };

  return (
    <EntityDialog
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="action"
      size="sm"
      title={t("Financial:ImportPriceList")}
      isBusy={importList.isPending}
      footer={
        <>
          <Button type="button" variant="outline" onClick={onClose} disabled={importList.isPending}>
            {t("Cancel")}
          </Button>
          <Button type="submit" form="import-price-list-form" disabled={importList.isPending}>
            {importList.isPending ? t("Financial:Importing") : t("Financial:Import")}
          </Button>
        </>
      }
    >
      <form id="import-price-list-form" onSubmit={(e) => void submit(e)} className="flex flex-col gap-3" noValidate>
        {serverError && (
          <Alert variant="destructive">
            <AlertDescription>{serverError}</AlertDescription>
          </Alert>
        )}
        <Alert variant="warning">
          <AlertDescription>{t("Financial:ImportReplacesRulesWarning")}</AlertDescription>
        </Alert>
        <FormField label={t("Financial:ImportFile")} htmlFor="import-file" required error={submitted ? fileError : undefined} floating="always" hint={`${t("Financial:ImportFileHint")} ${f.bytes(MAX_IMPORT_BYTES)}`}>
          {/* GAP-05 (file upload) has no library component yet: a native file input, as the avatar picker uses. */}
          <Input id="import-file" type="file" accept={`.xlsx,${XLSX_TYPE}`} onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
        </FormField>
      </form>
    </EntityDialog>
  );
}
