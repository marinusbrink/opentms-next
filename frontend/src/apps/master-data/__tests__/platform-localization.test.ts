/// <reference types="node" />
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

import { describe, expect, it } from "vitest";

import {
  DOCUMENT_TRANSFORMATION_KINDS,
  EMAIL_TEMPLATE_PURPOSES,
  NUMBER_SEQUENCE_SCHEMES,
  PLATFORM_CULTURES,
  REPORT_PURPOSES,
  REPORT_RENDERING_PROVIDERS,
  cultureKey,
} from "@/domains/platform/master-data";

/* Every Platform localization key the design-49 screens use exists in the Platform resource in both languages (nl,
 * en): the literal "Platform:…" keys of the new files, the enum keys of the domain module, the language tab keys and
 * the keys MasterDataEditor / MasterDataListView build from `resource` + entityKey (New…, …Saved, …Deleted,
 * Delete…, Delete…Question). A missing key shows the raw key to the user. Risk class: Low (texts). */

const SRC = resolve((import.meta as unknown as { dirname: string }).dirname, "../../..");
const REPO = resolve(SRC, "../..");

const FILES = [
  "app/apps.config.ts",
  "apps/master-data/editors/EmailTemplateEditor.tsx",
  "apps/master-data/editors/ReportDefinitionEditor.tsx",
  "apps/master-data/editors/DocumentTypeEditor.tsx",
  "apps/master-data/editors/NumberSequenceEditor.tsx",
  "apps/master-data/views/EmailTemplatesView.tsx",
  "apps/master-data/views/ReportDefinitionsView.tsx",
  "apps/master-data/views/DocumentTypesView.tsx",
  "apps/master-data/views/NumberSequencesView.tsx",
];

const ENTITIES = ["EmailTemplate", "ReportDefinition", "DocumentType", "NumberSequence"];

function texts(lang: string): Record<string, string> {
  const path = resolve(REPO, `backend/modules/platform/OpenTms.Platform.Domain.Shared/Localization/Platform/${lang}.json`);
  return (JSON.parse(readFileSync(path, "utf-8")) as { texts: Record<string, string> }).texts;
}

function literalKeys(): string[] {
  const keys = new Set<string>();
  for (const file of FILES) {
    const src = readFileSync(resolve(SRC, file), "utf-8");
    for (const match of src.matchAll(/["'`](Platform:[A-Za-z0-9.]+)["'`]/g)) keys.add(match[1]);
  }
  return [...keys];
}

const derivedKeys = (): string[] => [
  ...ENTITIES.flatMap((e) => [`Platform:${e}`, `Platform:${e}s`, `Platform:New${e}`, `Platform:${e}Saved`, `Platform:${e}Deleted`, `Platform:Delete${e}`, `Platform:Delete${e}Question`]),
  ...[EMAIL_TEMPLATE_PURPOSES, REPORT_PURPOSES, REPORT_RENDERING_PROVIDERS, DOCUMENT_TRANSFORMATION_KINDS, NUMBER_SEQUENCE_SCHEMES].flatMap((options) => options.map((o) => o.key)),
  ...PLATFORM_CULTURES.map((c) => cultureKey(c)),
  `Platform:Preview`,
];

describe("Platform master-data localization (design 49)", () => {
  const keys = [...new Set([...literalKeys(), ...derivedKeys()])].sort();

  it("finds the keys (the scan itself works)", () => {
    expect(keys.length).toBeGreaterThan(60);
    expect(keys).toContain("Platform:Nav.Platform");
    expect(keys).toContain("Platform:NumberSequenceInUseCannotDelete");
  });

  it.each(["en", "nl"])("every key has a non-empty text in %s.json", (lang) => {
    const resource = texts(lang);
    const missing = keys.filter((key) => !(resource[key] ?? "").trim());
    expect(missing, `missing in ${lang}.json`).toEqual([]);
  });

  it.each(["en", "nl"])("the texts that take a value have the {0} placeholder in %s.json", (lang) => {
    const resource = texts(lang);
    const withValue = [...ENTITIES.map((e) => `Platform:Delete${e}Question`), "Platform:ExampleLabel"];
    for (const key of withValue) expect(resource[key], key).toContain("{0}");
  });

  it("the Dutch nav texts are the design's", () => {
    const nl = texts("nl");
    expect(["Platform:Nav.Platform", "Platform:EmailTemplates", "Platform:ReportDefinitions", "Platform:DocumentTypes", "Platform:NumberSequences"].map((k) => nl[k])).toEqual([
      "Platform",
      "E-mailsjablonen",
      "Rapportdefinities",
      "Documentsoorten",
      "Nummerreeksen",
    ]);
  });
});
