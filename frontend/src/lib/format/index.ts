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
