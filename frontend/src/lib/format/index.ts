import { useMemo } from "react";

import { useL } from "@/lib/i18n/LocalizationProvider";

/* All date, number and amount output goes through here (UI-FMT-001). The UI
 * language maps to a formatting locale (UI-FMT-002; en is en-GB), the clock is
 * 24-hour, and timezone conversion happens only in this file (UI-FMT-005: the
 * browser timezone until D-24 is decided). */

const FORMATTING_LOCALES: Record<string, string> = {
  nl: "nl-NL",
  en: "en-GB",
  de: "de-DE",
  fr: "fr-FR",
  es: "es-ES",
  it: "it-IT",
  pt: "pt-PT",
  ru: "ru-RU",
};

export function formattingLocale(culture: string): string {
  const language = culture.toLowerCase().split("-")[0] ?? "";
  return FORMATTING_LOCALES[language] ?? culture;
}

export type DateInput = Date | string | number | null | undefined;

export interface NumberOptions {
  decimals?: number;
  maxDecimals?: number;
}

export interface Formatter {
  date(value: DateInput): string;
  dateTime(value: DateInput): string;
  time(value: DateInput): string;
  timestamp(value: DateInput): string;
  dateMedium(value: DateInput): string;
  number(value: number | null | undefined, options?: NumberOptions): string;
  amount(value: number | null | undefined): string;
  unitPrice(value: number | null | undefined): string;
  percent(value: number | null | undefined): string;
  duration(minutes: number | null | undefined): string;
  /** A span given in seconds, down to the second (heartbeats, timeouts); minutes and up read like `duration`. */
  durationSeconds(seconds: number | null | undefined): string;
  /** A byte count in the largest unit that keeps the figure above 1 (B, KB, MB, GB, TB), one decimal from KB up. */
  bytes(value: number | null | undefined): string;
}

const DATE_ONLY = /^\d{4}-\d{2}-\d{2}$/;

/** Parses API values. A date-only string is a calendar date: it is never shifted by the timezone. */
export function toDate(value: DateInput): Date | null {
  if (value == null || value === "") return null;
  if (value instanceof Date) return Number.isNaN(value.getTime()) ? null : value;
  if (typeof value === "string" && DATE_ONLY.test(value)) {
    const [year, month, day] = value.split("-").map(Number);
    return new Date(year, month - 1, day);
  }
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
}

const pad = (n: number) => String(n).padStart(2, "0");

export function createFormatter(culture: string, t: (key: string) => string): Formatter {
  const locale = formattingLocale(culture);

  const dateFormat = new Intl.DateTimeFormat(locale, { day: "2-digit", month: "2-digit", year: "numeric" });
  const mediumFormat = new Intl.DateTimeFormat(locale, { day: "numeric", month: "short", year: "numeric" });

  const number = (value: number | null | undefined, options: NumberOptions = {}) => {
    if (value == null || Number.isNaN(value)) return "";
    const minimumFractionDigits = options.decimals ?? 0;
    const maximumFractionDigits = Math.max(minimumFractionDigits, options.maxDecimals ?? options.decimals ?? 2);
    return new Intl.NumberFormat(locale, { minimumFractionDigits, maximumFractionDigits }).format(value);
  };

  const time = (value: DateInput) => {
    const d = toDate(value);
    return d ? `${pad(d.getHours())}:${pad(d.getMinutes())}` : "";
  };

  const date = (value: DateInput) => {
    const d = toDate(value);
    return d ? dateFormat.format(d) : "";
  };

  return {
    date,
    time,
    dateTime: (value) => {
      const d = toDate(value);
      return d ? `${dateFormat.format(d)} ${time(d)}` : "";
    },
    timestamp: (value) => {
      const d = toDate(value);
      if (!d) return "";
      return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
    },
    dateMedium: (value) => {
      const d = toDate(value);
      return d ? mediumFormat.format(d) : "";
    },
    number,
    amount: (value) => number(value, { decimals: 2 }),
    unitPrice: (value) => number(value, { decimals: 2, maxDecimals: 5 }),
    percent: (value) => {
      if (value == null || Number.isNaN(value)) return "";
      return new Intl.NumberFormat(locale, { style: "percent", maximumFractionDigits: 2 }).format(value / 100);
    },
    duration: (minutes) => {
      if (minutes == null || Number.isNaN(minutes)) return "";
      const total = Math.max(0, Math.round(minutes));
      const days = Math.floor(total / 1440);
      const hours = Math.floor((total % 1440) / 60);
      const mins = total % 60;
      const parts: string[] = [];
      if (days > 0) parts.push(`${days}${t("Duration:Day")}`);
      if (hours > 0) parts.push(`${hours}${t("Duration:Hour")}`);
      if (mins > 0 || parts.length === 0) parts.push(`${mins}${t("Duration:Minute")}`);
      return parts.join(" ");
    },
    durationSeconds: (seconds) => {
      if (seconds == null || Number.isNaN(seconds)) return "";
      const total = Math.max(0, Math.round(seconds));
      const days = Math.floor(total / 86400);
      const hours = Math.floor((total % 86400) / 3600);
      const mins = Math.floor((total % 3600) / 60);
      const secs = total % 60;
      const parts: string[] = [];
      if (days > 0) parts.push(`${days}${t("Duration:Day")}`);
      if (hours > 0) parts.push(`${hours}${t("Duration:Hour")}`);
      if (mins > 0) parts.push(`${mins}${t("Duration:Minute")}`);
      if (secs > 0 || parts.length === 0) parts.push(`${secs}${t("Duration:Second")}`);
      return parts.join(" ");
    },
    bytes: (value) => {
      if (value == null || Number.isNaN(value)) return "";
      const units = ["Unit:Bytes", "Unit:Kilobytes", "Unit:Megabytes", "Unit:Gigabytes", "Unit:Terabytes"];
      let size = Math.max(0, value);
      let index = 0;
      while (size >= 1024 && index < units.length - 1) {
        size /= 1024;
        index += 1;
      }
      return `${number(size, { decimals: index === 0 ? 0 : 1 })} ${t(units[index])}`;
    },
  };
}

export function useFormat(): Formatter {
  const { culture, t } = useL();
  return useMemo(() => createFormatter(culture, t), [culture, t]);
}

/* Date input (UI-FMT-004): the DatePicker shows and accepts `f.date`'s format and sends
 * `YYYY-MM-DD`. The pattern is derived from the formatting locale, never hard-coded. */

type DateField = "day" | "month" | "year";

interface DateInputPattern {
  order: DateField[];
  separator: string;
}

const FIELD_LENGTH: Record<DateField, number> = { day: 2, month: 2, year: 4 };
const FIELD_PLACEHOLDER: Record<DateField, string> = { day: "DD", month: "MM", year: "YYYY" };
const patternCache = new Map<string, DateInputPattern>();

function dateInputPatternOf(locale: string): DateInputPattern {
  const cached = patternCache.get(locale);
  if (cached) return cached;
  const parts = new Intl.DateTimeFormat(locale, { day: "2-digit", month: "2-digit", year: "numeric" }).formatToParts(
    new Date(2000, 0, 1),
  );
  const order = parts.map((p) => p.type).filter((type): type is DateField => type === "day" || type === "month" || type === "year");
  const firstIndex = parts.findIndex((p) => p.type === order[0]);
  const literal = parts.slice(firstIndex + 1).find((p) => p.type === "literal");
  const pattern: DateInputPattern = {
    order: order.length === 3 ? order : ["day", "month", "year"],
    separator: literal?.value.trim() || "-",
  };
  patternCache.set(locale, pattern);
  return pattern;
}

/** The typing pattern for a locale, e.g. `DD-MM-YYYY` (nl-NL) or `DD/MM/YYYY` (en-GB). */
export function dateInputPattern(locale: string): string {
  const { order, separator } = dateInputPatternOf(locale);
  return order.map((field) => FIELD_PLACEHOLDER[field]).join(separator);
}

/** Masks typed text to the locale's pattern: digits only, the separator inserted after each complete field. */
export function maskDateInput(text: string, locale: string): string {
  const { order, separator } = dateInputPatternOf(locale);
  let digits = text.replace(/\D/g, "").slice(0, 8);
  const fields: string[] = [];
  for (const field of order) {
    if (digits.length === 0) break;
    fields.push(digits.slice(0, FIELD_LENGTH[field]));
    digits = digits.slice(FIELD_LENGTH[field]);
  }
  const complete = fields.length > 0 && fields[fields.length - 1].length === FIELD_LENGTH[order[fields.length - 1]];
  const typedSeparator = /\D$/.test(text.trim());
  return fields.join(separator) + (complete && fields.length < order.length && typedSeparator ? separator : "");
}

/** Parses text typed in the locale's pattern to `YYYY-MM-DD`; `null` when incomplete or not a real date. */
export function parseDateInput(text: string, locale: string): string | null {
  const { order } = dateInputPatternOf(locale);
  const groups = text.trim().split(/\D+/).filter((g) => g.length > 0);
  if (groups.length !== 3) return null;
  const values = {} as Record<DateField, number>;
  for (let i = 0; i < 3; i += 1) {
    const field = order[i];
    const group = groups[i];
    if (field === "year" ? group.length !== 4 : group.length < 1 || group.length > 2) return null;
    values[field] = Number(group);
  }
  const { year, month, day } = values;
  if (month < 1 || month > 12 || day < 1 || day > 31) return null;
  const check = new Date(year, month - 1, day);
  if (check.getFullYear() !== year || check.getMonth() !== month - 1 || check.getDate() !== day) return null;
  return `${year}-${pad(month)}-${pad(day)}`;
}

/** Formats a `YYYY-MM-DD` value in the locale's pattern for the input; empty for null or invalid values. */
export function formatDateInput(isoDate: string | null | undefined, locale: string): string {
  if (!isoDate || !DATE_ONLY.test(isoDate)) return "";
  const [year, month, day] = isoDate.split("-");
  const { order, separator } = dateInputPatternOf(locale);
  const value: Record<DateField, string> = { day, month, year };
  return order.map((field) => value[field]).join(separator);
}

/** A calendar date as `YYYY-MM-DD` from its local components; never `toISOString()`, which shifts by the timezone. */
export function toIsoDate(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}
