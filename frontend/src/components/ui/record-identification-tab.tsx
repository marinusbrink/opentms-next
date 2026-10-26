import { Check, Copy, Fingerprint } from "lucide-react";
import { useState } from "react";

import { Button } from "@/components/ui/button";
import type { EntityDialogTab } from "@/components/ui/entity-dialog";
import { FormSection } from "@/components/ui/form-section";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The standard "Record identification" tab of every editor (UI-FORM-008, PO decision
 * 2026-10-10): the record's technical identity, read-only. Shows what the record exposes;
 * audit fields the DTO does not carry are simply absent. */
export interface RecordIdentification {
  id?: string | null;
  creationTime?: string | Date | null;
  creator?: string | null;
  lastModificationTime?: string | Date | null;
  modifier?: string | null;
  /** On a one-page record (design 61 "Order page, revised") the section is collapsible and starts closed. */
  collapsible?: boolean;
}

export function RecordIdentificationTab(record: RecordIdentification) {
  const { t } = useL();
  const f = useFormat();
  const [copied, setCopied] = useState(false);

  /* One topic, so the section carries the tab's own name (UI-FORM-005). */
  if (!record.id) {
    return (
      <FormSection title={t("RecordIdentification")} collapsible={record.collapsible}>
        <p className="text-sm text-muted-foreground">{t("Record:NotSavedYet")}</p>
      </FormSection>
    );
  }

  const copyId = async () => {
    try {
      await navigator.clipboard.writeText(record.id!);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      /* Clipboard access can be denied; the id stays selectable as text. */
    }
  };

  const rows: { label: string; value: string }[] = [
    ...(record.creationTime
      ? [{ label: t("Record:Created"), value: [f.timestamp(record.creationTime), record.creator].filter(Boolean).join(" · ") }]
      : []),
    ...(record.lastModificationTime
      ? [{ label: t("Record:LastModified"), value: [f.timestamp(record.lastModificationTime), record.modifier].filter(Boolean).join(" · ") }]
      : []),
  ];

  return (
    <FormSection title={t("RecordIdentification")} collapsible={record.collapsible}>
      <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-3 text-sm">
      <dt className="text-label font-medium text-muted-foreground">{t("Record:Id")}</dt>
      <dd className="flex items-center gap-2">
        <code className="rounded-md bg-background px-2 py-1 font-mono text-label select-all">{record.id}</code>
        <TooltipProvider>
          <Tooltip>
            <TooltipTrigger
              render={<Button variant="ghost" size="icon-sm" onClick={() => void copyId()} aria-label={t("Record:CopyId")} />}
            >
              {copied ? <Check className="text-tone-success" /> : <Copy />}
            </TooltipTrigger>
            <TooltipContent>{t("Record:CopyId")}</TooltipContent>
          </Tooltip>
        </TooltipProvider>
      </dd>
      {rows.map((row) => (
        <div key={row.label} className="contents">
          <dt className="text-label font-medium text-muted-foreground">{row.label}</dt>
          <dd className="tabular-nums">{row.value}</dd>
        </div>
      ))}
      </dl>
    </FormSection>
  );
}

/** The identification tab as an `EntityDialog` tab; always the last tab (UI-FORM-008). */
export function recordIdentificationTab(t: (key: string) => string, record: RecordIdentification): EntityDialogTab {
  return {
    value: "identification",
    label: t("RecordIdentification"),
    icon: Fingerprint,
    content: <RecordIdentificationTab {...record} />,
  };
}
