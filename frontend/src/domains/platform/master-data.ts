import { useMutation, useQuery } from "@tanstack/react-query";
import { useCallback } from "react";

import type { GridRequest, GridResponse, GridSelectionDto } from "@/components/ui/opentms-grid";
import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";
import type { NumberSequenceScheme } from "@/domains/platform/number-sequence-format";

/* Platform's master-data lists (docs/designs/49-platform-master-data.md): e-mail templates, report definitions,
 * document types and number sequences, shown in the Master data app's Platform group. The hooks mirror the Master
 * Data domain's entity-hook factory (design-41 list contract: one paged list with the flat query, a detail read, a
 * bounded lookup, create, update with the concurrency stamp, delete and bulk delete), without importing it:
 * Platform is the floor and depends on no domain module. Every call goes through the generated client. */

type Schemas = components["schemas"];

export type PlatformLookupItem = Schemas["OpenTms.Platform.PlatformMasterDataLookupDto"];
export type PlatformBulkDeleteResult = Schemas["OpenTms.Platform.PlatformMasterDataBulkDeleteResultDto"];

export const PLATFORM_LOOKUP_TAKE = 500;

/** The configured languages (nl, en) and the default one whose content is required (LocalizedName.DefaultCulture). */
export const PLATFORM_DEFAULT_CULTURE = "en";
export const PLATFORM_CULTURES = ["en", "nl"] as const;
export type PlatformCulture = (typeof PLATFORM_CULTURES)[number];
export const cultureKey = (culture: string | null | undefined) => `Platform:Language.${(culture ?? "").toLowerCase()}`;

/**
 * The query key of a Platform master-data list. The root is the Master data app's ("masterData", as
 * masterDataQueryKey): its shared editor invalidates `["masterData", entity]` after a save, and create in place
 * puts new records into `["masterData", entity, "lookup"]`.
 */
export function platformMasterDataQueryKey(entity: string, ...rest: unknown[]) {
  return ["masterData", entity, ...rest] as const;
}

export type PlatformMasterDataPath =
  | "/api/platform/email-template"
  | "/api/platform/report-definition"
  | "/api/platform/document-type"
  | "/api/platform/number-sequence";

type ListExtra = Record<string, string | number | boolean | null | undefined>;

interface LooseResult {
  data?: unknown;
  error?: unknown;
  response: Response;
}

interface LooseClient {
  GET: (path: string, init?: unknown) => Promise<LooseResult>;
  POST: (path: string, init?: unknown) => Promise<LooseResult>;
  PUT: (path: string, init?: unknown) => Promise<LooseResult>;
  DELETE: (path: string, init?: unknown) => Promise<LooseResult>;
}

/* The four paths differ only in their root; the entity exports below pin the DTO types to the generated schema. */
const client = apiClient as unknown as LooseClient;

export interface PlatformMasterDataHooks<TRow, TDto, TCreate, TUpdate, TExtra extends ListExtra> {
  entity: string;
  path: PlatformMasterDataPath;
  useGridFetcher: (extra?: TExtra) => (request: GridRequest) => Promise<GridResponse<TRow>>;
  useDetail: (id: string | undefined) => { data: TDto | undefined; isLoading: boolean; error: unknown };
  useLookup: (enabled?: boolean) => { data: PlatformLookupItem[] | undefined; isLoading: boolean; error: unknown };
  useCreate: () => { mutateAsync: (dto: TCreate) => Promise<TDto>; isPending: boolean };
  useUpdate: () => { mutateAsync: (args: { id: string; dto: TUpdate }) => Promise<TDto>; isPending: boolean };
  useDelete: () => { mutateAsync: (id: string) => Promise<void>; isPending: boolean };
  useBulkDelete: () => {
    mutateAsync: (args: { selection: GridSelectionDto; extra?: TExtra }) => Promise<PlatformBulkDeleteResult>;
    isPending: boolean;
  };
}

function createPlatformMasterDataHooks<TRow, TDto, TCreate, TUpdate, TExtra extends ListExtra = ListExtra>(
  entity: string,
  path: PlatformMasterDataPath,
): PlatformMasterDataHooks<TRow, TDto, TCreate, TUpdate, TExtra> {
  return {
    entity,
    path,

    useGridFetcher(extra?: TExtra) {
      const extraKey = JSON.stringify(extra ?? {});
      return useCallback(
        async (request: GridRequest): Promise<GridResponse<TRow>> => {
          const sortModel = request.sortModels[0];
          const { data, error } = await client.GET(path, {
            params: {
              query: {
                startRow: request.startRow,
                endRow: request.endRow,
                wildcardSearch: request.wildcardSearch ?? undefined,
                sortBy: sortModel?.colId,
                sortDir: sortModel?.sort,
                ...(JSON.parse(extraKey) as TExtra),
              },
            },
          });
          if (error) throw error;
          const page = data as { rows?: TRow[]; totalCount?: number; filteredCount?: number } | undefined;
          return {
            rows: page?.rows ?? [],
            totalCount: Number(page?.totalCount ?? 0),
            filteredCount: Number(page?.filteredCount ?? 0),
          };
        },
        // eslint-disable-next-line react-hooks/exhaustive-deps
        [extraKey],
      );
    },

    useDetail(id) {
      return useQuery({
        queryKey: platformMasterDataQueryKey(entity, "detail", id),
        enabled: id != null,
        queryFn: async (): Promise<TDto> => {
          const { data, error } = await client.GET(`${path}/{id}`, { params: { path: { id } } });
          if (error) throw error;
          return data as TDto;
        },
      });
    },

    useLookup(enabled = true) {
      return useQuery({
        queryKey: platformMasterDataQueryKey(entity, "lookup"),
        enabled,
        staleTime: 60 * 1000,
        queryFn: async (): Promise<PlatformLookupItem[]> => {
          const { data, error } = await client.GET(`${path}/lookup`, { params: { query: { take: PLATFORM_LOOKUP_TAKE } } });
          if (error) throw error;
          return (data as PlatformLookupItem[] | undefined) ?? [];
        },
      });
    },

    useCreate() {
      return useMutation({
        mutationFn: async (dto: TCreate): Promise<TDto> => {
          const { data, error } = await client.POST(path, { body: dto });
          if (error) throw error;
          return data as TDto;
        },
      });
    },

    useUpdate() {
      return useMutation({
        mutationFn: async ({ id, dto }: { id: string; dto: TUpdate }): Promise<TDto> => {
          const { data, error } = await client.PUT(`${path}/{id}`, { params: { path: { id } }, body: dto });
          if (error) throw error;
          return data as TDto;
        },
      });
    },

    useDelete() {
      return useMutation({
        mutationFn: async (id: string): Promise<void> => {
          const { error } = await client.DELETE(`${path}/{id}`, { params: { path: { id } } });
          if (error) throw error;
        },
      });
    },

    useBulkDelete() {
      return useMutation({
        mutationFn: async ({ selection, extra }: { selection: GridSelectionDto; extra?: TExtra }): Promise<PlatformBulkDeleteResult> => {
          const { data, error } = await client.POST(`${path}/bulk-delete`, {
            body: {
              selection: {
                mode: selection.mode,
                explicitIds: selection.explicitIds,
                excludedIds: selection.excludedIds,
                filterRequest: selection.filterRequest
                  ? {
                      startRow: selection.filterRequest.startRow,
                      endRow: selection.filterRequest.endRow,
                      wildcardSearch: selection.filterRequest.wildcardSearch,
                      sortModels: selection.filterRequest.sortModels,
                      columnFilters: selection.filterRequest.columnFilters,
                      rowGroupCols: selection.filterRequest.rowGroupCols,
                      groupKeys: selection.filterRequest.groupKeys,
                    }
                  : undefined,
              },
              ...(extra ?? {}),
            },
          });
          if (error) throw error;
          return (data as PlatformBulkDeleteResult | undefined) ?? { deletedCount: 0, skippedRows: [] };
        },
      });
    },
  };
}

/** The archivable lists' toolbar filter (report definitions, document types). */
export type IncludeArchivedExtra = { includeArchived?: boolean };

/** An enum's values with their localization keys, in the backend's order. */
export interface EnumOption<T extends number> {
  value: T;
  key: string;
}

const enumOptions = <T extends number>(prefix: string, names: readonly string[]): EnumOption<T>[] =>
  names.map((name, index) => ({ value: (index + 1) as T, key: `Platform:${prefix}.${name}` }));

const keyOf =
  <T extends number>(options: EnumOption<T>[]) =>
  (value: T | null | undefined): string =>
    options.find((o) => o.value === value)?.key ?? "";

// ---------------------------------------------------------------------------------------------------------------
// E-mail templates

export type EmailTemplatePurpose = Schemas["OpenTms.Platform.EmailTemplates.EmailTemplatePurpose"];
export type EmailTemplateRow = Schemas["OpenTms.Platform.EmailTemplates.EmailTemplateRowDto"];
export type EmailTemplateDto = Schemas["OpenTms.Platform.EmailTemplates.EmailTemplateDto"];
export type EmailTemplateCreateDto = Schemas["OpenTms.Platform.EmailTemplates.EmailTemplateCreateDto"];
export type EmailTemplateUpdateDto = Schemas["OpenTms.Platform.EmailTemplates.EmailTemplateUpdateDto"];
export type EmailTemplateContentInputDto = Schemas["OpenTms.Platform.EmailTemplates.EmailTemplateContentInputDto"];

export const emailTemplates = createPlatformMasterDataHooks<
  EmailTemplateRow,
  EmailTemplateDto,
  EmailTemplateCreateDto,
  EmailTemplateUpdateDto,
  { purpose?: EmailTemplatePurpose }
>("emailTemplates", "/api/platform/email-template");

export const EMAIL_TEMPLATE_PURPOSES = enumOptions<EmailTemplatePurpose>("EmailTemplatePurpose", [
  "TransportOrder",
  "TransportFile",
  "Invoice",
  "InvoiceReminder",
  "PurchaseOrder",
  "EtaNotification",
  "DriverInstructions",
  "DigitalArchive",
  "ProformaOverview",
]);
export const emailTemplatePurposeKey = keyOf(EMAIL_TEMPLATE_PURPOSES);

/** PlatformMasterDataConsts: address fields 512, subject 512, body 200,000 characters. */
export const MAX_EMAIL_ADDRESS_FIELD_LENGTH = 512;
export const MAX_SUBJECT_LENGTH = 512;
export const MAX_HTML_BODY_LENGTH = 200_000;

// ---------------------------------------------------------------------------------------------------------------
// Report definitions

export type ReportPurpose = Schemas["OpenTms.Platform.ReportDefinitions.ReportPurpose"];
export type ReportRenderingProvider = Schemas["OpenTms.Platform.ReportDefinitions.ReportRenderingProvider"];
export type ReportDefinitionRow = Schemas["OpenTms.Platform.ReportDefinitions.ReportDefinitionRowDto"];
export type ReportDefinitionDto = Schemas["OpenTms.Platform.ReportDefinitions.ReportDefinitionDto"];
export type ReportDefinitionCreateDto = Schemas["OpenTms.Platform.ReportDefinitions.ReportDefinitionCreateDto"];
export type ReportDefinitionUpdateDto = Schemas["OpenTms.Platform.ReportDefinitions.ReportDefinitionUpdateDto"];

export const reportDefinitions = createPlatformMasterDataHooks<
  ReportDefinitionRow,
  ReportDefinitionDto,
  ReportDefinitionCreateDto,
  ReportDefinitionUpdateDto,
  IncludeArchivedExtra & { purpose?: ReportPurpose }
>("reportDefinitions", "/api/platform/report-definition");

export const REPORT_PURPOSES = enumOptions<ReportPurpose>("ReportPurpose", [
  "TransportOrderConfirmation",
  "TransportFile",
  "Invoice",
  "PurchaseOrder",
  "TransportUnitBalance",
]);
export const reportPurposeKey = keyOf(REPORT_PURPOSES);

export const REPORT_RENDERING_PROVIDERS = enumOptions<ReportRenderingProvider>("ReportRenderingProvider", ["PdfGeneratorApi"]);
export const reportRenderingProviderKey = keyOf(REPORT_RENDERING_PROVIDERS);

/** PlatformMasterDataConsts: provider template id 128, file name pattern 255 characters. */
export const MAX_PROVIDER_TEMPLATE_ID_LENGTH = 128;
export const MAX_FILE_NAME_PATTERN_LENGTH = 255;

export function useArchiveReportDefinition() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST("/api/platform/report-definition/{id}/archive", { params: { path: { id } } });
      if (error) throw error;
    },
  });
}

export function useUnarchiveReportDefinition() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST("/api/platform/report-definition/{id}/unarchive", { params: { path: { id } } });
      if (error) throw error;
    },
  });
}

// ---------------------------------------------------------------------------------------------------------------
// Document types

export type DocumentTransformationKind = Schemas["OpenTms.Platform.DocumentTypes.DocumentTransformationKind"];
export type DocumentTypeRow = Schemas["OpenTms.Platform.DocumentTypes.DocumentTypeRowDto"];
export type DocumentTypeDto = Schemas["OpenTms.Platform.DocumentTypes.DocumentTypeDto"];
export type DocumentTypeCreateDto = Schemas["OpenTms.Platform.DocumentTypes.DocumentTypeCreateDto"];
export type DocumentTypeUpdateDto = Schemas["OpenTms.Platform.DocumentTypes.DocumentTypeUpdateDto"];

export const documentTypes = createPlatformMasterDataHooks<
  DocumentTypeRow,
  DocumentTypeDto,
  DocumentTypeCreateDto,
  DocumentTypeUpdateDto,
  IncludeArchivedExtra
>("documentTypes", "/api/platform/document-type");

export const DOCUMENT_TRANSFORMATION_KINDS = enumOptions<DocumentTransformationKind>("DocumentTransformationKind", [
  "ImageToPdf",
  "LinkByFileName",
  "RenameByParentProperty",
  "SplitPdfByReferencePattern",
]);
export const documentTransformationKindKey = keyOf(DOCUMENT_TRANSFORMATION_KINDS);

/** The kinds whose argument is required (RenameByParentProperty: the property; SplitPdfByReferencePattern: the regex). */
export const KINDS_WITH_ARGUMENT: readonly DocumentTransformationKind[] = [3, 4];

/** PlatformMasterDataConsts: code 32, argument 255 characters, at most four transformations (one per kind). */
export const MAX_DOCUMENT_TYPE_CODE_LENGTH = 32;
export const MAX_TRANSFORMATION_ARGUMENT_LENGTH = 255;
export const MAX_DOCUMENT_TYPE_TRANSFORMATIONS = 4;

export function useArchiveDocumentType() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST("/api/platform/document-type/{id}/archive", { params: { path: { id } } });
      if (error) throw error;
    },
  });
}

export function useUnarchiveDocumentType() {
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.POST("/api/platform/document-type/{id}/unarchive", { params: { path: { id } } });
      if (error) throw error;
    },
  });
}

// ---------------------------------------------------------------------------------------------------------------
// Number sequences

export type NumberSequenceRow = Schemas["OpenTms.Platform.NumberSequences.NumberSequenceRowDto"];
export type NumberSequenceDto = Schemas["OpenTms.Platform.NumberSequences.NumberSequenceDto"];
export type NumberSequenceCreateDto = Schemas["OpenTms.Platform.NumberSequences.NumberSequenceCreateDto"];
export type NumberSequenceUpdateDto = Schemas["OpenTms.Platform.NumberSequences.NumberSequenceUpdateDto"];
export type { NumberSequenceScheme };

export const numberSequences = createPlatformMasterDataHooks<
  NumberSequenceRow,
  NumberSequenceDto,
  NumberSequenceCreateDto,
  NumberSequenceUpdateDto
>("numberSequences", "/api/platform/number-sequence");

export const NUMBER_SEQUENCE_SCHEMES = enumOptions<NumberSequenceScheme>("NumberSequenceScheme", [
  "FourDigitYear",
  "TwoDigitYear",
  "FourDigitYearMonth",
  "TwoDigitYearMonth",
  "CounterOnly",
]);
export const numberSequenceSchemeKey = keyOf(NUMBER_SEQUENCE_SCHEMES);

/** PlatformMasterDataConsts.MaxNameLength / MaxDescriptionLength, shared by the four lists. */
export const MAX_PLATFORM_NAME_LENGTH = 128;
export const MAX_PLATFORM_DESCRIPTION_LENGTH = 2500;
