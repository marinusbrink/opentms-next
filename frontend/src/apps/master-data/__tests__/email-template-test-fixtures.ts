import type { EmailTemplateDefaults, EmailTemplateTagGroup } from "@/domains/platform/master-data";

/* Fixtures for the e-mail template editor's Tags panel and default content (design 54, "Tags and default content",
 * PO 2026-10-20): the shapes `GET /api/platform/email-template/tags` and `/defaults` return, as the generated client
 * types them. Not a test file: vitest's include pattern matches *.test.* only. */

/** Three groups: two of values, one collection with children (the shape the catalogue builds for `freight`). */
export const TAG_GROUPS: EmailTemplateTagGroup[] = [
  {
    group: "company",
    label: "Own company",
    tags: [
      { path: "company.name", kind: 1, label: "Name", description: "The own company's name", snippet: "{{ company.name }}" },
      { path: "company.email", kind: 1, label: "E-mail", description: null, snippet: "{{ company.email }}" },
    ],
  },
  {
    group: "order",
    label: "Order",
    tags: [
      { path: "order.number", kind: 1, label: "Number", description: "The order number", snippet: "{{ order.number }}" },
      { path: "order.reference", kind: 1, label: "Reference", description: "Your reference on the order", snippet: "{{ order.reference }}" },
    ],
  },
  {
    group: "freight",
    label: "Freight",
    tags: [
      {
        path: "freight",
        kind: 2,
        label: "Freight lines",
        description: "The order's lines, one block per line",
        snippet: "{{ for line in freight }}\n{{ line.product }}\n{{ line.quantity }}\n{{ end }}",
        children: [
          { path: "freight.product", kind: 1, label: "Product", description: "The product of the line", snippet: "{{ line.product }}" },
          { path: "freight.quantity", kind: 1, label: "Quantity", description: null, snippet: "{{ line.quantity }}" },
        ],
      },
    ],
  },
];

/** The defaults per (purpose, culture), keyed as `defaultsKey` builds it: "" is All languages (no culture sent). */
export const DEFAULTS: Record<string, EmailTemplateDefaults> = {
  "1:": {
    senderAddress: "{{ company.email }}",
    replyTo: "{{ company.email }}",
    subject: "Order {{ order.number }} confirmed",
    htmlBody: "<p>Dear {{ recipient.salutation }}, we confirm order {{ order.number }}.</p>",
  },
  "1:nl": {
    senderAddress: "{{ company.email }}",
    replyTo: "{{ company.email }}",
    subject: "Order {{ order.number }} bevestigd",
    htmlBody: "<p>Geachte {{ recipient.salutation }}, wij bevestigen order {{ order.number }}.</p>",
  },
  "1:en": {
    senderAddress: "{{ company.email }}",
    replyTo: "{{ company.email }}",
    subject: "Order {{ order.number }} confirmed (en)",
    htmlBody: "<p>Dear {{ recipient.salutation }}, order {{ order.number }} (en).</p>",
  },
  "3:": {
    senderAddress: "{{ company.invoice_email }}",
    replyTo: "{{ company.invoice_email }}",
    subject: "Invoice {{ invoice.number }}",
    htmlBody: "<p>Please find invoice {{ invoice.number }} attached.</p>",
  },
};

export const defaultsKey = (query: Record<string, unknown> | undefined): string => `${String(query?.purpose ?? "")}:${String(query?.culture ?? "")}`;

/** A promise the test resolves by hand (a response that arrives when the test says so). */
export function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((r) => {
    resolve = r;
  });
  return { promise, resolve };
}
