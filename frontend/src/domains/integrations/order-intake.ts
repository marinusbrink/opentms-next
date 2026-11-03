import { useCallback } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import type { GridRequest, GridResponse } from "@/components/ui/opentms-grid";
import { apiClient } from "@/lib/api/client";
import { withStatus } from "@/lib/api/errors";
import type { components } from "@/lib/api/generated/schema";
import { useFeatureState } from "@/lib/auth/permissions";
import { fallbackFileName, fileNameFromContentDisposition, saveBlob } from "@/lib/browser";

/* Order intake on the Integrations side (design 72, "Channels", "Inbound store", "Import profiles"; build note 1 for the
 * singular routes): the mailbox connections with their OAuth connect and folder choice, the API clients with their
 * one-time secret, the import profiles (read-only rules), and the received messages with their documents, step log,
 * originals and reprocessing. Every call goes through the generated client; the hooks narrow the DTO types to the
 * generated schema. The screens additionally check the tenant feature `Integrations.OrderIntake` (ORDER_INTAKE_FEATURE). */

type Schemas = components["schemas"];

export type MailboxRow = Schemas["OpenTms.Integrations.OrderIntake.MailboxConnectionRowDto"];
export type MailboxDto = Schemas["OpenTms.Integrations.OrderIntake.MailboxConnectionDto"];
export type MailboxUpdateDto = Schemas["OpenTms.Integrations.OrderIntake.MailboxConnectionUpdateDto"];
export type MailboxFolder = Schemas["OpenTms.Integrations.OrderIntake.MailboxFolderDto"];
export type MailProvider = Schemas["OpenTms.Integrations.OrderIntake.MailProvider"];
export type MailboxStatus = Schemas["OpenTms.Integrations.OrderIntake.MailboxStatus"];

export type ApiClientRow = Schemas["OpenTms.Integrations.OrderIntake.ApiClientRowDto"];
export type ApiClientDto = Schemas["OpenTms.Integrations.OrderIntake.ApiClientDto"];
export type ApiClientCreateDto = Schemas["OpenTms.Integrations.OrderIntake.ApiClientCreateDto"];
export type ApiClientUpdateDto = Schemas["OpenTms.Integrations.OrderIntake.ApiClientUpdateDto"];
export type ApiClientSecret = Schemas["OpenTms.Integrations.OrderIntake.ApiClientSecretDto"];

export type ImportProfileRow = Schemas["OpenTms.Integrations.OrderIntake.ImportProfileRowDto"];
export type ImportProfileDto = Schemas["OpenTms.Integrations.OrderIntake.ImportProfileDto"];
export type ImportProfileStatus = Schemas["OpenTms.Integrations.OrderIntake.ImportProfileStatus"];
export type ImportProfileRules = Schemas["OpenTms.Integrations.OrderIntake.Profiles.ImportProfileRules"];
export type FieldRule = Schemas["OpenTms.Integrations.OrderIntake.Profiles.FieldRule"];
export type FieldTransform = Schemas["OpenTms.Integrations.OrderIntake.Profiles.FieldTransform"];

export type InboundMessageRow = Schemas["OpenTms.Integrations.OrderIntake.InboundMessageRowDto"];
export type InboundMessageDto = Schemas["OpenTms.Integrations.OrderIntake.InboundMessageDto"];
export type InboundDocumentDto = Schemas["OpenTms.Integrations.OrderIntake.InboundDocumentDto"];
export type InboundDocumentStep = Schemas["OpenTms.Integrations.OrderIntake.InboundDocumentStepDto"];
export type InboundDocumentIntake = Schemas["OpenTms.Integrations.OrderIntake.InboundDocumentIntakeDto"];
export type SourceLocation = Schemas["OpenTms.Integrations.OrderIntake.SourceLocation"];
export type InboundChannel = Schemas["OpenTms.Integrations.OrderIntake.InboundChannel"];
export type InboundMessageStatus = Schemas["OpenTms.Integrations.OrderIntake.InboundMessageStatus"];
export type DocumentFormat = Schemas["OpenTms.Integrations.OrderIntake.DocumentFormat"];

/** The tenant feature of the whole intake (design 72 "Features", default off); every screen of this run checks it. */
export const ORDER_INTAKE_FEATURE = "Integrations.OrderIntake";

/**
 * Whether the tenant has order intake (UI-PERM-006: a screen behind an off feature renders FeatureUnavailable): "loading"
 * until the configuration is known, so a screen never flashes the unavailable state.
 */
export function useOrderIntakeFeature(): "loading" | "on" | "off" {
  return useFeatureState(ORDER_INTAKE_FEATURE);
}

/* ── Enums: value → name; the localization key is `Integrations:<Enum>.<name>` ─────────────────────────────────── */

export const MAIL_PROVIDERS = { 1: "Gmail", 2: "Microsoft365" } as const;
export const MAILBOX_STATUSES = { 1: "AwaitingConsent", 2: "AwaitingFolder", 3: "Active", 4: "Paused", 5: "Reconnect" } as const;
export const IMPORT_PROFILE_STATUSES = { 1: "Proposed", 2: "Active", 3: "Retired" } as const;
export const PROFILE_ORIGINS = { 1: "Ai", 2: "Repair" } as const;
export const INBOUND_CHANNELS = { 1: "Gmail", 2: "Microsoft365", 3: "Api" } as const;
export const INBOUND_MESSAGE_STATUSES = { 1: "Received", 2: "Processing", 3: "Completed", 4: "NeedsAttention", 5: "Failed", 6: "NotAnOrder" } as const;
export const INBOUND_DOCUMENT_KINDS = { 1: "MailBody", 2: "Attachment", 3: "ApiJson", 4: "ApiFile" } as const;
export const INBOUND_DOCUMENT_STATUSES = {
  1: "Pending",
  2: "Mapped",
  3: "Submitted",
  4: "Completed",
  5: "NeedsReview",
  6: "NeedsAttention",
  7: "NotAnOrder",
  8: "Skipped",
  9: "Failed",
} as const;
export const DOCUMENT_FORMATS = { 0: "Unknown", 1: "Text", 2: "Html", 3: "Pdf", 4: "ScannedPdf", 5: "Xlsx", 6: "Csv", 7: "Xml", 8: "Json" } as const;
export const MAPPING_METHODS = { 0: "None", 1: "Profile", 2: "AiVerifiedProfile", 3: "AiExtraction", 4: "Canonical" } as const;
export const INTAKE_STEPS = {
  1: "Received",
  2: "Stored",
  3: "Extracted",
  4: "Fingerprinted",
  5: "ProfileMatched",
  6: "ProfileNotFound",
  7: "ProfileFailed",
  8: "AiRequested",
  9: "AiCompleted",
  10: "AiFailed",
  11: "ProfileProposed",
  12: "ProfileVerified",
  13: "ProfileVerificationFailed",
  14: "NotAnOrder",
  15: "Mapped",
  16: "Submitted",
  17: "OrderCreated",
  18: "NeedsReview",
  19: "Resolved",
  20: "Rejected",
  21: "OrderConfirmed",
  22: "Skipped",
  23: "Failed",
  24: "Reprocessed",
} as const;
export const STEP_OUTCOMES = { 1: "Info", 2: "Success", 3: "Warning", 4: "Error" } as const;
export const SOURCE_LOCATION_KINDS = { 1: "Cell", 2: "Line", 3: "Path", 4: "Constant", 5: "Json" } as const;
export const FIELD_SOURCE_KINDS = { 1: "column", 2: "cell", 3: "xpath", 4: "label", 5: "regex", 6: "constant" } as const;
export const FIELD_TRANSFORM_KINDS = { 1: "trim", 2: "date", 3: "time", 4: "number", 5: "map", 6: "split", 7: "join", 8: "upper" } as const;

/** The name of an enum value, or undefined for an unknown one. */
export function enumName(map: Readonly<Record<number, string>>, value: number | null | undefined): string | undefined {
  return value == null ? undefined : map[value];
}

/** The localized label of an enum value: `Integrations:<enumKey>.<name>`; an unknown value shows as its number. */
export function enumLabel(t: (key: string) => string, enumKey: string, map: Readonly<Record<number, string>>, value: number | null | undefined): string {
  const name = enumName(map, value);
  return name ? t(`Integrations:${enumKey}.${name}`) : value == null ? "" : String(value);
}

/** Select / MultiSelect options of an enum, in value order. */
export function enumOptions(t: (key: string) => string, enumKey: string, map: Readonly<Record<number, string>>): { id: string; label: string }[] {
  return Object.entries(map).map(([value, name]) => ({ id: value, label: t(`Integrations:${enumKey}.${name}`) }));
}

/* Parameters that carry an enum name get its localized text (the step log stores names, never texts). */
const PARAMETER_ENUMS: Record<string, string> = { channel: "InboundChannel", format: "DocumentFormat", method: "MappingMethod" };

/** One step's text (design 72 "Inbound store"): the step's localization key with its `{name}` parameters filled in. */
export function stepText(t: (key: string) => string, step: Pick<InboundDocumentStep, "messageKey" | "parameters" | "step">): string {
  const key = step.messageKey;
  const template = key ? t(key) : enumLabel(t, "IntakeStep", INTAKE_STEPS, step.step);
  const parameters = step.parameters ?? {};
  return template.replace(/\{(\w+)\}/g, (match, name: string) => {
    const value = parameters[name];
    if (value == null) return match;
    const enumKey = PARAMETER_ENUMS[name];
    if (enumKey) {
      const localized = t(`Integrations:${enumKey}.${value}`);
      if (localized !== `Integrations:${enumKey}.${value}`) return localized;
    }
    return value;
  });
}

/** The text of a mailbox's last sync error: `Mailboxes:Error.<code>`, else the code itself. */
export function mailboxErrorText(t: (key: string) => string, code: string | null | undefined): string {
  if (!code) return "";
  const key = `Mailboxes:Error.${code}`;
  const text = t(key);
  return text === key ? code : text;
}

/* ── Query keys ──────────────────────────────────────────────────────────────────────────────────────────────── */

export const orderIntakeKeys = {
  mailboxes: ["integrations", "mailboxes"] as const,
  mailbox: (id: string | undefined) => ["integrations", "mailboxes", "detail", id?.toLowerCase()] as const,
  folders: (id: string | undefined) => ["integrations", "mailboxes", "folders", id?.toLowerCase()] as const,
  apiClients: ["integrations", "apiClients"] as const,
  apiClient: (id: string | undefined) => ["integrations", "apiClients", "detail", id?.toLowerCase()] as const,
  importProfiles: ["integrations", "importProfiles"] as const,
  importProfile: (id: string | undefined) => ["integrations", "importProfiles", "detail", id?.toLowerCase()] as const,
  inboundMessages: ["integrations", "inboundMessages"] as const,
  inboundMessage: (id: string | undefined) => ["integrations", "inboundMessages", "detail", id?.toLowerCase()] as const,
  settings: ["integrations", "orderIntakeSettings"] as const,
};

/* ── Shared ──────────────────────────────────────────────────────────────────────────────────────────────────── */

const toPage = <TRow,>(data: { rows?: TRow[] | null; totalCount?: number; filteredCount?: number } | undefined): GridResponse<TRow> => ({
  rows: data?.rows ?? [],
  totalCount: Number(data?.totalCount ?? 0),
  filteredCount: Number(data?.filteredCount ?? 0),
});

const paging = (request: GridRequest) => {
  const sort = request.sortModels[0];
  return {
    StartRow: request.startRow,
    EndRow: request.endRow,
    WildcardSearch: request.wildcardSearch ?? undefined,
    SortBy: sort?.colId,
    SortDir: sort?.sort,
  };
};

/** Throws the ABP error with its HTTP status (a record page tells "not found" from it, recordLoadState). */
const unwrap = <T,>(result: { data?: T; error?: unknown; response?: Response }): T => {
  if (result.error) throw withStatus(result.error, result.response);
  return result.data as T;
};

/** A grid fetcher that stays the same while its filters do (the grid refetches on a new one). */
function useFetcher<TFilters, TRow>(filters: TFilters, fetch: (request: GridRequest, filters: TFilters) => Promise<GridResponse<TRow>>, refreshKey = 0) {
  const key = JSON.stringify(filters);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  return useCallback((request: GridRequest) => fetch(request, JSON.parse(key) as TFilters), [key, refreshKey]);
}

/** Saves a file the API streams (document original, raw message) under the server's name, else the fallback. */
async function download(result: { data?: unknown; error?: unknown; response: Response }, fallback: string): Promise<void> {
  if (result.error) throw result.error;
  const blob = result.data as Blob | undefined;
  if (!blob) throw new Error("The server returned no file.");
  const contentType = result.response.headers.get("Content-Type") ?? blob.type;
  saveBlob(blob, fileNameFromContentDisposition(result.response.headers.get("Content-Disposition")) ?? fallbackFileName(fallback, contentType, ""));
}

/* ── Mailboxes ───────────────────────────────────────────────────────────────────────────────────────────────── */

export interface MailboxListFilters {
  status?: MailboxStatus[];
}

export async function fetchMailboxes(request: GridRequest, filters: MailboxListFilters): Promise<GridResponse<MailboxRow>> {
  const { data, error } = await apiClient.GET("/api/integrations/mailbox-connection", {
    params: { query: { ...paging(request), Status: filters.status?.length ? filters.status : undefined } },
  });
  if (error) throw error;
  return toPage<MailboxRow>(data);
}

export function useMailboxesGridFetcher(filters: MailboxListFilters, refreshKey = 0) {
  return useFetcher(filters, fetchMailboxes, refreshKey);
}

export function useMailbox(id: string | undefined) {
  return useQuery({
    queryKey: orderIntakeKeys.mailbox(id),
    enabled: id != null,
    queryFn: async () => unwrap(await apiClient.GET("/api/integrations/mailbox-connection/{id}", { params: { path: { id: id! } } })),
  });
}

/** The provider's labels or folders of a connected mailbox (needs Integrations.Mailboxes.Manage). */
export function useMailboxFolders(id: string | undefined, enabled = true) {
  return useQuery({
    queryKey: orderIntakeKeys.folders(id),
    enabled: id != null && enabled,
    staleTime: 60 * 1000,
    retry: false,
    queryFn: async (): Promise<MailboxFolder[]> => unwrap(await apiClient.GET("/api/integrations/mailbox-connection/{id}/folders", { params: { path: { id: id! } } })) ?? [],
  });
}

function useMailboxMutation<TArgs, TResult>(run: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: orderIntakeKeys.mailboxes }),
  });
}

/** Connect: creates the row (waiting for sign-in) and returns the provider's authorize URL the browser goes to. */
export function useConnectMailbox() {
  return useMailboxMutation(async (input: { provider: MailProvider; displayName: string }) =>
    unwrap(await apiClient.POST("/api/integrations/mailbox-connection", { body: input })),
  );
}

/** Reconnect (a revoked or never completed sign-in): the authorize URL again. */
export function useReconnectMailbox() {
  return useMailboxMutation(async (id: string) => unwrap(await apiClient.POST("/api/integrations/mailbox-connection/{id}/reconnect", { params: { path: { id } } })));
}

export function useUpdateMailbox() {
  return useMailboxMutation(async ({ id, dto }: { id: string; dto: MailboxUpdateDto }) =>
    unwrap(await apiClient.PUT("/api/integrations/mailbox-connection/{id}", { params: { path: { id } }, body: dto })),
  );
}

export function usePauseMailbox() {
  return useMailboxMutation(async (id: string) => unwrap(await apiClient.POST("/api/integrations/mailbox-connection/{id}/pause", { params: { path: { id } } })));
}

export function useResumeMailbox() {
  return useMailboxMutation(async (id: string) => unwrap(await apiClient.POST("/api/integrations/mailbox-connection/{id}/resume", { params: { path: { id } } })));
}

export function useDeleteMailbox() {
  return useMailboxMutation(async (id: string) => {
    unwrap(await apiClient.DELETE("/api/integrations/mailbox-connection/{id}", { params: { path: { id } } }));
  });
}

/* ── API clients ─────────────────────────────────────────────────────────────────────────────────────────────── */

export interface ApiClientListFilters {
  isActive?: boolean;
}

export async function fetchApiClients(request: GridRequest, filters: ApiClientListFilters): Promise<GridResponse<ApiClientRow>> {
  const { data, error } = await apiClient.GET("/api/integrations/api-client", { params: { query: { ...paging(request), IsActive: filters.isActive } } });
  if (error) throw error;
  return toPage<ApiClientRow>(data);
}

export function useApiClientsGridFetcher(filters: ApiClientListFilters, refreshKey = 0) {
  return useFetcher(filters, fetchApiClients, refreshKey);
}

export function useApiClient(id: string | undefined) {
  return useQuery({
    queryKey: orderIntakeKeys.apiClient(id),
    enabled: id != null,
    queryFn: async () => unwrap(await apiClient.GET("/api/integrations/api-client/{id}", { params: { path: { id: id! } } })),
  });
}

function useApiClientMutation<TArgs, TResult>(run: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: orderIntakeKeys.apiClients }),
  });
}

/** Create: the answer carries the secret, shown once and never again. */
export function useCreateApiClient() {
  return useApiClientMutation(async (dto: ApiClientCreateDto) => unwrap(await apiClient.POST("/api/integrations/api-client", { body: dto })));
}

export function useUpdateApiClient() {
  return useApiClientMutation(async ({ id, dto }: { id: string; dto: ApiClientUpdateDto }) =>
    unwrap(await apiClient.PUT("/api/integrations/api-client/{id}", { params: { path: { id } }, body: dto })),
  );
}

/** Regenerate: replaces the secret; the answer carries the new one, shown once. */
export function useRegenerateApiClientSecret() {
  return useApiClientMutation(async (id: string) => unwrap(await apiClient.POST("/api/integrations/api-client/{id}/regenerate-secret", { params: { path: { id } } })));
}

export function useActivateApiClient() {
  return useApiClientMutation(async (id: string) => unwrap(await apiClient.POST("/api/integrations/api-client/{id}/activate", { params: { path: { id } } })));
}

export function useDeactivateApiClient() {
  return useApiClientMutation(async (id: string) => unwrap(await apiClient.POST("/api/integrations/api-client/{id}/deactivate", { params: { path: { id } } })));
}

export function useDeleteApiClient() {
  return useApiClientMutation(async (id: string) => {
    unwrap(await apiClient.DELETE("/api/integrations/api-client/{id}", { params: { path: { id } } }));
  });
}

/** The example call of the one-time secret dialog: a client-credentials token request with the scope. */
export function tokenCurlExample(secret: Pick<ApiClientSecret, "tokenUrl" | "clientId" | "clientSecret" | "scope">): string {
  return [
    `curl -X POST "${secret.tokenUrl ?? ""}" \\`,
    `  -H "Content-Type: application/x-www-form-urlencoded" \\`,
    `  -d "grant_type=client_credentials" \\`,
    `  -d "client_id=${secret.clientId ?? ""}" \\`,
    `  -d "client_secret=${secret.clientSecret ?? ""}" \\`,
    `  -d "scope=${secret.scope ?? ""}"`,
  ].join("\n");
}

/* ── Import profiles ─────────────────────────────────────────────────────────────────────────────────────────── */

export interface ImportProfileListFilters {
  status?: ImportProfileStatus[];
  format?: DocumentFormat;
}

export async function fetchImportProfiles(request: GridRequest, filters: ImportProfileListFilters): Promise<GridResponse<ImportProfileRow>> {
  const { data, error } = await apiClient.GET("/api/integrations/import-profile", {
    params: { query: { ...paging(request), Status: filters.status?.length ? filters.status : undefined, Format: filters.format } },
  });
  if (error) throw error;
  return toPage<ImportProfileRow>(data);
}

export function useImportProfilesGridFetcher(filters: ImportProfileListFilters, refreshKey = 0) {
  return useFetcher(filters, fetchImportProfiles, refreshKey);
}

export function useImportProfile(id: string | undefined) {
  return useQuery({
    queryKey: orderIntakeKeys.importProfile(id),
    enabled: id != null,
    queryFn: async () => unwrap(await apiClient.GET("/api/integrations/import-profile/{id}", { params: { path: { id: id! } } })),
  });
}

function useImportProfileMutation<TArgs, TResult>(run: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: (dto) => {
      const saved = dto as ImportProfileDto | undefined;
      if (saved?.id) queryClient.setQueryData(orderIntakeKeys.importProfile(saved.id), saved);
      void queryClient.invalidateQueries({ queryKey: orderIntakeKeys.importProfiles });
    },
  });
}

export function useActivateImportProfile() {
  return useImportProfileMutation(async (id: string) => unwrap(await apiClient.POST("/api/integrations/import-profile/{id}/activate", { params: { path: { id } } })));
}

export function useRetireImportProfile() {
  return useImportProfileMutation(async (id: string) => unwrap(await apiClient.POST("/api/integrations/import-profile/{id}/retire", { params: { path: { id } } })));
}

export function useRenameImportProfile() {
  return useImportProfileMutation(async ({ id, name, concurrencyStamp }: { id: string; name: string; concurrencyStamp: string }) =>
    unwrap(await apiClient.POST("/api/integrations/import-profile/{id}/rename", { params: { path: { id } }, body: { name, concurrencyStamp } })),
  );
}

export function useDeleteImportProfile() {
  return useImportProfileMutation(async (id: string) => {
    unwrap(await apiClient.DELETE("/api/integrations/import-profile/{id}", { params: { path: { id } } }));
  });
}

/* ── Received messages and the trace ─────────────────────────────────────────────────────────────────────────── */

export interface InboundMessageListFilters {
  status?: InboundMessageStatus[];
  channel?: InboundChannel[];
  receivedFrom?: string;
  receivedTo?: string;
}

export async function fetchInboundMessages(request: GridRequest, filters: InboundMessageListFilters): Promise<GridResponse<InboundMessageRow>> {
  const { data, error } = await apiClient.GET("/api/integrations/inbound-message", {
    params: {
      query: {
        ...paging(request),
        Status: filters.status?.length ? filters.status : undefined,
        Channel: filters.channel?.length ? filters.channel : undefined,
        ReceivedFrom: filters.receivedFrom || undefined,
        ReceivedTo: filters.receivedTo || undefined,
      },
    },
  });
  if (error) throw error;
  return toPage<InboundMessageRow>(data);
}

export function useInboundMessagesGridFetcher(filters: InboundMessageListFilters, refreshKey = 0) {
  return useFetcher(filters, fetchInboundMessages, refreshKey);
}

export function useInboundMessage(id: string | undefined) {
  return useQuery({
    queryKey: orderIntakeKeys.inboundMessage(id),
    enabled: id != null,
    queryFn: async () => unwrap(await apiClient.GET("/api/integrations/inbound-message/{id}", { params: { path: { id: id! } } })),
  });
}

/** The message a document belongs to (design 72: the review's "View source" and the order's source link know only the
 *  document). */
export function useInboundMessageByDocument(documentId: string | undefined) {
  return useQuery({
    queryKey: ["integrations", "inboundMessages", "byDocument", documentId?.toLowerCase()],
    enabled: documentId != null,
    queryFn: async () => unwrap(await apiClient.GET("/api/integrations/inbound-message/by-document/{documentId}", { params: { path: { documentId: documentId! } } })),
  });
}

/** Downloads a document's original file. */
export async function downloadInboundDocument(document: Pick<InboundDocumentDto, "id" | "fileName">): Promise<void> {
  const result = await apiClient.GET("/api/integrations/inbound-message/document-content/{documentId}", {
    params: { path: { documentId: document.id ?? "" } },
    parseAs: "blob",
  });
  await download(result, document.fileName ?? "document");
}

/** Downloads the raw message (the .eml of a mail, the request of an API call). */
export async function downloadInboundMessageRaw(message: Pick<InboundMessageDto, "id">): Promise<void> {
  const result = await apiClient.GET("/api/integrations/inbound-message/{id}/raw-content", { params: { path: { id: message.id ?? "" } }, parseAs: "blob" });
  await download(result, "message");
}

/** Reprocess one document: the pipeline runs it again (intakes Orders already holds are not created twice). */
export function useReprocessInboundDocument() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (documentId: string) => unwrap(await apiClient.POST("/api/integrations/inbound-documents/{id}/reprocess", { params: { path: { id: documentId } } })),
    onSuccess: (dto) => {
      if (dto?.id) queryClient.setQueryData(orderIntakeKeys.inboundMessage(dto.id), dto);
      void queryClient.invalidateQueries({ queryKey: orderIntakeKeys.inboundMessages });
    },
  });
}

/* ── Settings (design 72 build note 25): the tenant's name similarity threshold ──────────────────────────────────── */

export type OrderIntakeSettings = Schemas["OpenTms.Integrations.OrderIntake.OrderIntakeSettingsDto"];

/** The threshold's range and step (OrderIntakeConsts): the settings page offers exactly these values. */
export const NAME_SIMILARITY_MIN = 0.5;
export const NAME_SIMILARITY_MAX = 0.95;
export const NAME_SIMILARITY_STEP = 0.05;
export const NAME_SIMILARITY_DEFAULT = 0.75;

/** 0.50, 0.55, … 0.95, computed in hundredths so no floating-point drift creeps in. */
export const NAME_SIMILARITY_VALUES: readonly number[] = Array.from(
  { length: Math.round((NAME_SIMILARITY_MAX - NAME_SIMILARITY_MIN) / NAME_SIMILARITY_STEP) + 1 },
  (_, i) => Math.round(NAME_SIMILARITY_MIN * 100 + i * NAME_SIMILARITY_STEP * 100) / 100,
);

export function useOrderIntakeSettings(enabled = true) {
  return useQuery({
    queryKey: orderIntakeKeys.settings,
    enabled,
    queryFn: async () => unwrap(await apiClient.GET("/api/integrations/order-intake-settings")),
  });
}

export function useUpdateOrderIntakeSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: OrderIntakeSettings) => unwrap(await apiClient.PUT("/api/integrations/order-intake-settings", { body: input })),
    onSuccess: (dto) => {
      if (dto) queryClient.setQueryData(orderIntakeKeys.settings, dto);
      void queryClient.invalidateQueries({ queryKey: orderIntakeKeys.settings });
    },
  });
}
