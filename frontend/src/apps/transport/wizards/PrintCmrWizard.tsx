import { useEffect, useMemo, useState } from "react";
import { Download, Printer } from "lucide-react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Wizard, type WizardTask } from "@/components/ui/wizard";
import {
  CMR_PARTY_BOXES,
  CMR_PARTY_SOURCES,
  deliverBlob,
  downloadConsignmentNote,
  printConsignmentNote,
  useConsignmentNoteProfile,
  useConsignmentNoteProfiles,
  type CmrPartyBoxField,
  type CmrPartySource,
  type ConsignmentNoteDocumentDto,
  type ConsignmentNoteOverridesDto,
} from "@/domains/orders/consignment-notes";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import { getApiErrorMessage } from "@/lib/api/errors";
import { parseInteger } from "@/lib/forms/use-form-state";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useQueryClient } from "@tanstack/react-query";

/* The Print CMR wizard (design 61; UI-WIZ-001 to -005): Profile (the default preselected, the party boxes and the
 * copies/borders/QR/group-by-leg flags overridable for this print) → Lines (the orders and lines to include; a trip
 * prints its orders as a whole) → Output: one print per order (design 58, build note 11: one document per order), each
 * a task in the TaskList, then a Download and a Print (opens the PDF) button per document. */

export interface PrintableOrder {
  id: string;
  number: string;
  lines: { id: string; position: number; label: string }[];
}

export interface PrintCmrWizardProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The orders (with their lines) to print; or a trip, printed as a whole. */
  orders?: PrintableOrder[];
  trip?: { id: string; number: string };
}

interface Overrides {
  copies: string;
  showBorders: boolean;
  showQrCode: boolean;
  groupByLeg: boolean;
  parties: Partial<Record<CmrPartyBoxField, CmrPartySource>>;
}

export function PrintCmrWizard({ open, onOpenChange, orders, trip }: PrintCmrWizardProps) {
  const { t } = useL();
  const queryClient = useQueryClient();
  const profiles = useConsignmentNoteProfiles(open);
  const [profileId, setProfileId] = useState<string | null>(null);
  const profile = useConsignmentNoteProfile(profileId ?? undefined);
  const [overrides, setOverrides] = useState<Overrides>({ copies: "", showBorders: true, showQrCode: true, groupByLeg: false, parties: {} });
  const [selectedLines, setSelectedLines] = useState<Set<string>>(new Set());
  const [tasks, setTasks] = useState<WizardTask[] | undefined>(undefined);
  const [documents, setDocuments] = useState<ConsignmentNoteDocumentDto[]>([]);
  const [busyId, setBusyId] = useState<string | null>(null);
  /* "Add new profile…" (UI-LOOK-004): the profile editor opens on top; the new one is picked and named until the rows refetch. */
  const inPlace = useCreateInPlace();
  const [created, setCreated] = useState<{ value: string; label: string } | null>(null);

  /* The default profile is preselected; the profile's values are the starting overrides. */
  useEffect(() => {
    if (!open) return;
    const rows = profiles.data ?? [];
    if (profileId == null && rows.length > 0) setProfileId((rows.find((p) => p.isDefault) ?? rows[0]).id ?? null);
  }, [open, profiles.data, profileId]);
  useEffect(() => {
    if (!profile.data) return;
    const p = profile.data;
    setOverrides({
      copies: String(p.copies ?? 1),
      showBorders: p.showBorders ?? true,
      showQrCode: p.showQrCode ?? true,
      groupByLeg: p.groupByLeg ?? false,
      parties: Object.fromEntries(CMR_PARTY_BOXES.map((box) => [box.field, p[box.field]])) as Overrides["parties"],
    });
  }, [profile.data]);
  useEffect(() => {
    if (open) {
      setSelectedLines(new Set((orders ?? []).flatMap((o) => o.lines.map((l) => l.id))));
      setTasks(undefined);
      setDocuments([]);
    }
  }, [open, orders]);

  const profileOptions = useMemo(() => {
    const options = (profiles.data ?? []).map((p) => ({ value: p.id ?? "", label: p.isDefault ? `${p.name ?? ""} (${t("MasterData:IsDefault")})` : (p.name ?? "") }));
    return created && !options.some((o) => o.value === created.value) ? [...options, created] : options;
  }, [profiles.data, created, t]);
  const partyItems = CMR_PARTY_SOURCES.map((s) => ({ value: String(s.value), label: t(s.key) }));

  const overridesDto = (): ConsignmentNoteOverridesDto => ({
    copies: parseInteger(overrides.copies),
    showBorders: overrides.showBorders,
    showQrCode: overrides.showQrCode,
    groupByLeg: overrides.groupByLeg,
    ...overrides.parties,
  });

  const selectedOrders = (orders ?? []).map((o) => ({ ...o, lines: o.lines.filter((l) => selectedLines.has(l.id)) })).filter((o) => o.lines.length > 0);
  const linesValid = trip != null || selectedOrders.length > 0;

  const run = async () => {
    const items = trip ? [{ id: trip.id, label: `${t("PlanningExecution:Trip")} ${trip.number}` }] : selectedOrders.map((o) => ({ id: o.id, label: `${t("Orders:TransportOrder")} ${o.number}` }));
    setTasks(items.map((item) => ({ id: item.id, label: item.label, status: "waiting" })));
    const printed: ConsignmentNoteDocumentDto[] = [];
    for (const item of items) {
      setTasks((prev) => prev?.map((task) => (task.id === item.id ? { ...task, status: "running" } : task)));
      try {
        const result = await printConsignmentNote({
          tripId: trip?.id ?? null,
          orderLineIds: trip ? null : selectedOrders.find((o) => o.id === item.id)?.lines.map((l) => l.id),
          profileId,
          overrides: overridesDto(),
        });
        printed.push(...result);
        setDocuments([...printed]);
        setTasks((prev) => prev?.map((task) => (task.id === item.id ? { ...task, status: "done" } : task)));
      } catch (err) {
        setTasks((prev) => prev?.map((task) => (task.id === item.id ? { ...task, status: "failed", reason: getApiErrorMessage(err, t) } : task)));
      }
    }
    void queryClient.invalidateQueries({ queryKey: ["orders", "consignmentNotes"] });
    if (printed.length > 0) toast.success(t("Orders:ConsignmentNotePrinted"));
  };

  const deliver = async (document: ConsignmentNoteDocumentDto, mode: "open" | "save") => {
    setBusyId(document.id ?? null);
    try {
      const { blob, fileName } = await downloadConsignmentNote(document.id!);
      deliverBlob(blob, document.fileName ?? fileName, mode);
    } catch (err) {
      toast.error(getApiErrorMessage(err, t));
    } finally {
      setBusyId(null);
    }
  };

  const toggleLine = (id: string, checked: boolean) =>
    setSelectedLines((prev) => {
      const next = new Set(prev);
      if (checked) next.add(id);
      else next.delete(id);
      return next;
    });

  return (
    <Wizard
      open={open}
      onOpenChange={onOpenChange}
      title={t("Orders:PrintConsignmentNote")}
      finishLabel={t("Orders:Print")}
      busyLabel={t("Orders:Printing")}
      onFinish={run}
      tasks={tasks}
      runContent={
        documents.length > 0 ? (
          <ul className="flex shrink-0 flex-col gap-2" data-slot="printed-documents">
            {documents.map((document) => (
              <li key={document.id} className="flex items-center gap-3 rounded-lg border bg-background px-3 py-2 text-sm">
                <span className="font-medium">{document.number}</span>
                <span className="text-muted-foreground">{document.orderNumber}</span>
                <span className="ml-auto flex gap-2">
                  <Button type="button" size="sm" variant="outline" disabled={busyId === document.id} onClick={() => void deliver(document, "save")}>
                    <Download /> {t("Orders:Download")}
                  </Button>
                  <Button type="button" size="sm" variant="outline" disabled={busyId === document.id} onClick={() => void deliver(document, "open")}>
                    <Printer /> {t("Orders:Print")}
                  </Button>
                </span>
              </li>
            ))}
          </ul>
        ) : undefined
      }
      steps={[
        {
          id: "profile",
          title: t("Orders:Profile"),
          isValid: profileId != null,
          content: (
            <div className="flex flex-col gap-4">
              <p className="text-sm text-muted-foreground">{t("Orders:PrintCmrIntro")}</p>
              <FormSection title={t("Orders:Profile")}>
                <div className="grid grid-cols-4 gap-x-4 gap-y-3">
                  <FormField label={t("Orders:ConsignmentNoteProfile")} htmlFor="cmr-profile" required floating="always" className="col-span-2">
                    <ReferenceCombobox
                      id="cmr-profile"
                      value={profileId}
                      onChange={setProfileId}
                      options={profileOptions}
                      required
                      {...inPlace.offer("ConsignmentNoteProfile", tasks != null, (r) => {
                        setCreated({ value: r.id, label: r.name });
                        setProfileId(r.id);
                      })}
                    />
                  </FormField>
                  <FormField label={t("Orders:Copies")} htmlFor="cmr-copies">
                    <Input id="cmr-copies" inputMode="numeric" value={overrides.copies} onChange={(e) => setOverrides({ ...overrides, copies: e.target.value })} />
                  </FormField>
                  <div className="flex flex-col justify-center gap-1">
                    {(["showBorders", "showQrCode", "groupByLeg"] as const).map((flag) => (
                      <label key={flag} className="flex items-center gap-2 text-sm" htmlFor={`cmr-${flag}`}>
                        <Checkbox id={`cmr-${flag}`} checked={overrides[flag]} onCheckedChange={(next) => setOverrides({ ...overrides, [flag]: next === true })} />
                        {t(`Orders:${flag[0].toUpperCase()}${flag.slice(1)}`)}
                      </label>
                    ))}
                  </div>
                </div>
              </FormSection>
              <FormSection title={t("Orders:Overrides")}>
                <div className="grid grid-cols-4 gap-x-4 gap-y-3">
                  {CMR_PARTY_BOXES.map((box) => (
                    <FormField key={box.field} label={t(box.key)} htmlFor={`cmr-${box.field}`} floating="always">
                      <Select
                        items={partyItems}
                        value={overrides.parties[box.field] != null ? String(overrides.parties[box.field]) : null}
                        onValueChange={(next) => setOverrides({ ...overrides, parties: { ...overrides.parties, [box.field]: next == null ? undefined : (Number(next) as CmrPartySource) } })}
                      >
                        <SelectTrigger id={`cmr-${box.field}`} className="h-10 w-full bg-background">
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          {partyItems.map((item) => (
                            <SelectItem key={item.value} value={item.value}>
                              {item.label}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </FormField>
                  ))}
                </div>
              </FormSection>
              {inPlace.dialog}
            </div>
          ),
        },
        {
          id: "lines",
          title: t("Orders:Lines"),
          isValid: linesValid,
          content: trip ? (
            <p className="text-sm text-muted-foreground">{t("Orders:PrintCmrTripHint").replace("{0}", trip.number)}</p>
          ) : (
            <div className="flex flex-col gap-3">
              <p className="text-sm text-muted-foreground">{t("Orders:PrintCmrLinesHint")}</p>
              {(orders ?? []).map((order) => (
                <FormSection key={order.id} title={`${t("Orders:TransportOrder")} ${order.number}`}>
                  <div className="grid grid-cols-2 gap-1">
                    {order.lines.map((line) => (
                      <label key={line.id} className="flex items-center gap-2 text-sm" htmlFor={`cmr-line-${line.id}`}>
                        <Checkbox id={`cmr-line-${line.id}`} checked={selectedLines.has(line.id)} onCheckedChange={(next) => toggleLine(line.id, next === true)} />
                        {t("Orders:Line")} {line.position}: {line.label}
                      </label>
                    ))}
                    {order.lines.length === 0 && <span className="text-label text-muted-foreground">{t("Orders:NoLines")}</span>}
                  </div>
                </FormSection>
              ))}
            </div>
          ),
        },
        {
          id: "output",
          title: t("Orders:Output"),
          content: <p className="text-sm text-muted-foreground">{t("Orders:PrintCmrOutputHint").replace("{0}", String(trip ? 1 : selectedOrders.length))}</p>,
        },
      ]}
    />
  );
}
