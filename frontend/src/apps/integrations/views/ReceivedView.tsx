import { useMemo, useState } from "react";
import type { ColDef } from "ag-grid-community";
import { FileSearch } from "lucide-react";

import { useAppFeatures } from "@/app/app-features";
import { AppCommandBar } from "@/components/ui/app-command-bar";
import { DatePicker } from "@/components/ui/date-picker";
import { FeatureUnavailable } from "@/components/ui/feature-unavailable";
import { DateTimeCell, NumberCell } from "@/components/ui/grid-cells";
import { MultiSelect } from "@/components/ui/multi-select";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { PermissionDenied } from "@/components/ui/permission-denied";
import { StatusCell } from "@/components/ui/status-badge";
import {
  INBOUND_CHANNELS,
  INBOUND_MESSAGE_STATUSES,
  enumLabel,
  enumName,
  enumOptions,
  useInboundMessagesGridFetcher,
  useOrderIntakeFeature,
  type InboundChannel,
  type InboundMessageRow,
  type InboundMessageStatus,
} from "@/domains/integrations/order-intake";
import { useIsGranted } from "@/lib/auth/permissions";
import { toIsoDate } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useOpenRecord } from "@/lib/use-open-record";

/* Integrations app → Received (design 72 "UI design"): every message that came in through a mailbox or the API, newest
 * first, on a full-height server-side grid (UI-PAGE-001) with the filters status and channel and the received date range
 * (UI-GRID-005: a transactional list starts on the last six months). Opening a row opens its trace page
 * (/integrations/received/{id}) through useOpenRecord. Order intake is a tenant feature (UI-PERM-006). */

function sixMonthsAgo(): string {
  const date = new Date();
  date.setMonth(date.getMonth() - 6);
  return toIsoDate(date);
}

export function ReceivedView() {
  const { t } = useL();
  const features = useAppFeatures();
  const feature = useOrderIntakeFeature();
  const openRecord = useOpenRecord();
  const canView = useIsGranted("Integrations.InboundMessages");
  const [statuses, setStatuses] = useState<InboundMessageStatus[]>([]);
  const [channels, setChannels] = useState<InboundChannel[]>([]);
  const [receivedFrom, setReceivedFrom] = useState<string | null>(sixMonthsAgo);
  const [receivedTo, setReceivedTo] = useState<string | null>(null);
  const filters = useMemo(
    () => ({ status: statuses, channel: channels, receivedFrom: receivedFrom ?? undefined, receivedTo: receivedTo ?? undefined }),
    [statuses, channels, receivedFrom, receivedTo],
  );
  const fetchRows = useInboundMessagesGridFetcher(filters);

  const columns = useMemo<ColDef<InboundMessageRow>[]>(
    () => [
      { field: "receivedAt", headerName: t("Integrations:ReceivedAt"), minWidth: 150, cellRenderer: DateTimeCell },
      { field: "channel", headerName: t("Integrations:Channel"), minWidth: 120, valueFormatter: (p) => enumLabel(t, "InboundChannel", INBOUND_CHANNELS, p.value as number) },
      { field: "sourceName", headerName: t("Integrations:Source"), minWidth: 160, sortable: false },
      { field: "from", headerName: t("Integrations:From"), flex: 1, minWidth: 180 },
      { field: "subject", headerName: t("Integrations:Subject"), flex: 2, minWidth: 200 },
      { field: "documentCount", headerName: t("Integrations:DocumentCount"), minWidth: 100, cellRenderer: NumberCell, type: "rightAligned", sortable: false },
      { field: "status", headerName: t("Integrations:Status"), minWidth: 140, cellRenderer: (p: { value: number | undefined }) => <StatusCell entity="InboundMessage" value={enumName(INBOUND_MESSAGE_STATUSES, p.value)} /> },
    ],
    [t],
  );

  if (feature === "off" || (features.isLoaded && !features.isFeatureEnabled("Apps.Integrations.Received"))) return <FeatureUnavailable />;
  if (!canView) return <PermissionDenied />;

  return (
    <div className="flex h-full flex-col" data-slot="received-view">
      <AppCommandBar commands={[]} />
      <div className="min-h-0 flex-1 px-6 pb-6">
        <LazyOpenTmsGrid<InboundMessageRow>
          gridId="integrations.inboundMessages"
          columnDefs={columns}
          fetchRows={fetchRows}
          onOpen={(row) => row.id && openRecord("InboundMessage", row.id)}
          rowActions={(rows) => (rows[0]?.id ? [{ id: "open", labelKey: "Integrations:ViewTrace", icon: FileSearch, onClick: () => openRecord("InboundMessage", rows[0].id!) }] : [])}
          filters={
            <>
              <DatePicker id="received-from-filter" value={receivedFrom} onChange={setReceivedFrom} placeholder={t("Integrations:ReceivedFrom")} className="w-36" />
              <DatePicker id="received-to-filter" value={receivedTo} onChange={setReceivedTo} placeholder={t("Integrations:ReceivedTo")} className="w-36" />
              <MultiSelect
                id="received-status-filter"
                value={statuses.map(String)}
                onChange={(next) => setStatuses(next.map(Number) as InboundMessageStatus[])}
                options={enumOptions(t, "InboundMessageStatus", INBOUND_MESSAGE_STATUSES)}
                className="w-44"
              />
              <MultiSelect
                id="received-channel-filter"
                value={channels.map(String)}
                onChange={(next) => setChannels(next.map(Number) as InboundChannel[])}
                options={enumOptions(t, "InboundChannel", INBOUND_CHANNELS)}
                className="w-40"
              />
            </>
          }
        />
      </div>
    </div>
  );
}
