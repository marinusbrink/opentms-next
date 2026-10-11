import { describe, expect, it } from "vitest";

import { createFormatter, formattingLocale, toDate } from "@/lib/format";

/* UI-FMT-002/003/006/009 (medium risk): the formats every grid and form shows. */

const t = (key: string) => ({ "Duration:Day": "d", "Duration:Hour": "h", "Duration:Minute": "m" })[key] ?? key;
const tNl = (key: string) => ({ "Duration:Day": "d", "Duration:Hour": "u", "Duration:Minute": "m" })[key] ?? key;

const stamp = new Date(2026, 9, 9, 14, 30, 15); // 9 October 2026, 14:30:15 local time

describe("formattingLocale", () => {
  it("maps en to en-GB (European conventions, never en-US)", () => {
    expect(formattingLocale("en")).toBe("en-GB");
    expect(formattingLocale("EN")).toBe("en-GB");
  });

  it("maps nl to nl-NL and leaves unknown cultures as they are", () => {
    expect(formattingLocale("nl")).toBe("nl-NL");
    expect(formattingLocale("pl-PL")).toBe("pl-PL");
  });
});

describe("createFormatter (en)", () => {
  const f = createFormatter("en", t);

  it("formats a date numerically, day first", () => {
    expect(f.date(stamp)).toBe("09/10/2026");
  });

  it("formats a date-time with a 24-hour clock and no comma (not 10/9/2026, 2:30 PM)", () => {
    expect(f.dateTime(stamp)).toBe("09/10/2026 14:30");
  });

  it("formats the technical timestamp culture-independently", () => {
    expect(f.timestamp(stamp)).toBe("2026-10-09 14:30:15");
  });

  it("formats amounts with two decimals and grouping, no symbol", () => {
    expect(f.amount(1234.5)).toBe("1,234.50");
  });

  it("formats unit prices with two to five decimals", () => {
    expect(f.unitPrice(0.12345)).toBe("0.12345");
    expect(f.unitPrice(2)).toBe("2.00");
  });

  it("formats percentages from whole numbers", () => {
    expect(f.percent(12.5)).toBe("12.5%");
  });

  it("formats durations compactly and omits zero parts", () => {
    expect(f.duration(1565)).toBe("1d 2h 5m");
    expect(f.duration(0)).toBe("0m");
    expect(f.duration(120)).toBe("2h");
  });

  it("returns an empty string for missing values", () => {
    expect(f.date(null)).toBe("");
    expect(f.amount(undefined)).toBe("");
    expect(f.dateTime("not a date")).toBe("");
  });
});

describe("createFormatter (nl)", () => {
  const f = createFormatter("nl", tNl);

  it("uses Dutch separators", () => {
    expect(f.date(stamp)).toBe("09-10-2026");
    expect(f.dateTime(stamp)).toBe("09-10-2026 14:30");
    expect(f.amount(1234.5)).toBe("1.234,50");
    expect(f.number(1234567, { decimals: 0 })).toBe("1.234.567");
  });

  it("localizes the duration unit letters", () => {
    expect(f.duration(1565)).toBe("1d 2u 5m");
  });
});

describe("toDate", () => {
  it("keeps a date-only API value on its calendar day instead of shifting it by the timezone", () => {
    const d = toDate("2026-10-09")!;
    expect(d.getFullYear()).toBe(2026);
    expect(d.getMonth()).toBe(9);
    expect(d.getDate()).toBe(9);
  });
});

describe("createFormatter.bytes", () => {
  const tUnits = (key: string) =>
    ({ "Unit:Bytes": "B", "Unit:Kilobytes": "KB", "Unit:Megabytes": "MB", "Unit:Gigabytes": "GB", "Unit:Terabytes": "TB" })[key] ?? key;
  const f = createFormatter("en", tUnits);

  it("picks the largest unit that keeps the figure above one and shows one decimal from KB up", () => {
    expect(f.bytes(0)).toBe("0 B");
    expect(f.bytes(512)).toBe("512 B");
    expect(f.bytes(1024)).toBe("1.0 KB");
    expect(f.bytes(1536)).toBe("1.5 KB");
    expect(f.bytes(5 * 1024 * 1024)).toBe("5.0 MB");
    expect(f.bytes(3.25 * 1024 ** 3)).toBe("3.3 GB");
    expect(f.bytes(2 * 1024 ** 4)).toBe("2.0 TB");
  });

  it("returns an empty string for nothing", () => {
    expect(f.bytes(null)).toBe("");
    expect(f.bytes(undefined)).toBe("");
  });

  it("uses the locale's decimal separator", () => {
    expect(createFormatter("nl", tUnits).bytes(1536)).toBe("1,5 KB");
  });
});

describe("createFormatter.durationSeconds", () => {
  const tDur = (key: string) =>
    ({ "Duration:Day": "d", "Duration:Hour": "h", "Duration:Minute": "m", "Duration:Second": "s" })[key] ?? key;
  const f = createFormatter("en", tDur);

  it("reads down to the second and drops empty parts", () => {
    expect(f.durationSeconds(30)).toBe("30s");
    expect(f.durationSeconds(90)).toBe("1m 30s");
    expect(f.durationSeconds(3600)).toBe("1h");
    expect(f.durationSeconds(90061)).toBe("1d 1h 1m 1s");
    expect(f.durationSeconds(0)).toBe("0s");
    expect(f.durationSeconds(null)).toBe("");
  });
});

