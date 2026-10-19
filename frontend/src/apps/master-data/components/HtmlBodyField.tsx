import { lazy, Suspense } from "react";

import { FormField } from "@/components/ui/form-field";
import { Skeleton } from "@/components/ui/skeleton";
import { unescapeScribanRegions } from "@/apps/master-data/components/scriban-html";

/* The one place an HTML body is edited (design 49, the e-mail template): the RichTextEditor (Tiptap, GAP-08, PO
 * 2026-10-18) in a FormField (UI-FORM-001). The visual view is the preview; the source view keeps raw HTML and Scriban
 * blocks editable, and a body the visual view cannot hold without loss opens there (preserveUnsupportedMarkup).
 * Scriban expressions typed in the visual view reach the server un-escaped (scriban-html.ts).
 * The editor is loaded lazily, so Tiptap is its own chunk and loads only when an e-mail template editor opens; the
 * skeleton has the editor's height, so the dialog does not jump. */

const RichTextEditor = lazy(() => import("@/components/ui/rich-text-editor").then((m) => ({ default: m.RichTextEditor })));

export interface HtmlBodyFieldProps {
  /** The HTML source with Scriban expressions. */
  value: string;
  onChange: (value: string) => void;
  readOnly: boolean;
  /** The translated label. */
  label: string;
  /** The translated error under the field (shown after blur or submit). */
  error?: string;
  /** The control's id, for the label. */
  id: string;
  required?: boolean;
  maxLength?: number;
  onBlur?: () => void;
}

export function HtmlBodyField({ value, onChange, readOnly, label, error, id, required, maxLength, onBlur }: HtmlBodyFieldProps) {
  return (
    <Suspense
      fallback={
        <FormField label={label} htmlFor={id} required={required} error={error} floating="always">
          <Skeleton className="h-96 w-full rounded-lg bg-muted" />
        </FormField>
      }
    >
      <FormField label={label} htmlFor={id} required={required} error={error} floating="always">
        <RichTextEditor
          id={id}
          aria-labelledby={`${id}-label`}
          value={value}
          maxLength={maxLength}
          readOnly={readOnly}
          onChange={onChange}
          onBlur={onBlur}
          preserveUnsupportedMarkup
          transformOutput={unescapeScribanRegions}
          className="h-96"
        />
      </FormField>
    </Suspense>
  );
}
