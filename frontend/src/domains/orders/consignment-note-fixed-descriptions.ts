import type { components } from "@/lib/api/generated/schema";
import { createReferenceDataHooks } from "@/lib/reference-data/crud-hooks";

/* The fixed texts a CMR profile prints in boxes 6 and 13 (design 58 "CMR": ConsignmentNoteFixedDescription). Design-50
 * list contract; a text in use by a profile is not deleted but archived. */

type Schemas = components["schemas"];

/** ConsignmentNoteConsts.MaxFixedDescriptionTextLength. */
export const MAX_FIXED_DESCRIPTION_TEXT_LENGTH = 2000;

export type ConsignmentNoteFixedDescriptionRow = Schemas["OpenTms.Orders.ConsignmentNotes.ConsignmentNoteFixedDescriptionRowDto"];
export type ConsignmentNoteFixedDescriptionDto = Schemas["OpenTms.Orders.ConsignmentNotes.ConsignmentNoteFixedDescriptionDto"];
export type ConsignmentNoteFixedDescriptionCreateDto = Schemas["OpenTms.Orders.ConsignmentNotes.ConsignmentNoteFixedDescriptionCreateDto"];
export type ConsignmentNoteFixedDescriptionUpdateDto = Schemas["OpenTms.Orders.ConsignmentNotes.ConsignmentNoteFixedDescriptionUpdateDto"];

export const consignmentNoteFixedDescriptions = createReferenceDataHooks<
  ConsignmentNoteFixedDescriptionRow,
  ConsignmentNoteFixedDescriptionDto,
  ConsignmentNoteFixedDescriptionCreateDto,
  ConsignmentNoteFixedDescriptionUpdateDto,
  { includeArchived?: boolean }
>("consignmentNoteFixedDescriptions", "/api/orders/consignment-note-fixed-description");
