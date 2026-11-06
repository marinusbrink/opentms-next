/// <reference types="node" />
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

import { describe, expect, it } from "vitest";

/* Design 74: what the AI connector screen reads from outside the frontend exists there. Every key the screen uses is in the nl and
 * en resource with the same placeholders, and the status numbers are the ones of the backend's enum and the committed contract.
 * Read from the sources, so a key or a value changed on one side fails here. Risk class: Low (texts), Medium (the enum: a wrong
 * number shows a wrong status). */

const FRONTEND = resolve((import.meta as unknown as { dirname: string }).dirname, "../../../..");
const REPO_ROOT = resolve(FRONTEND, "..");
const INTEGRATIONS = "backend/modules/integrations/OpenTms.Integrations.Domain.Shared";

const read = (path: string) => readFileSync(resolve(REPO_ROOT, path), "utf-8");
const integrationsTexts = (lang: string) => (JSON.parse(read(`${INTEGRATIONS}/Localization/Integrations/${lang}.json`)) as { texts: Record<string, string> }).texts;
const platformTexts = (lang: string) => (JSON.parse(read(`backend/modules/platform/OpenTms.Platform.Domain.Shared/Localization/Platform/${lang}.json`)) as { texts: Record<string, string> }).texts;
const productTexts = (lang: string) => {
  const parsed = JSON.parse(read(`backend/src/OpenTms.Domain.Shared/Localization/OpenTms/${lang}.json`)) as { texts?: Record<string, string>; Texts?: Record<string, string> };
  return parsed.texts ?? parsed.Texts ?? {};
};

const KEY = /"((?:AiConnector|Integrations):[A-Za-z0-9_.]+)"/g;
const keysIn = (source: string) => [...source.matchAll(KEY)].map((match) => match[1]);

/** The keys of one entity's block in the status badge's registry. */
function registryKeys(entity: string): string[] {
  const source = read("frontend/src/components/ui/status-badge.tsx");
  const block = new RegExp(`\\b${entity}: \\{([\\s\\S]*?)\\n  \\},`).exec(source);
  if (!block) throw new Error(`no registry block ${entity}`);
  return keysIn(block[1]);
}

/** Every key of the screen; a count text also has its `.One` (lib/i18n/count-text). */
function screenKeys(): string[] {
  const view = read("frontend/src/apps/integrations/views/AiConnectorView.tsx");
  const counted = [...view.matchAll(/countText\(t, "([^"]+)"/g)].map((match) => `${match[1]}.One`);
  const nav = read("frontend/src/app/apps.config.ts").includes('"Integrations:Nav.AiConnector"') ? ["Integrations:Nav.AiConnector"] : [];
  return [...new Set([...keysIn(view), ...counted, ...registryKeys("AiConnection"), ...registryKeys("AiConnectorSharing"), ...nav])].sort();
}

const placeholders = (text: string) => [...new Set(text.match(/\{\d+\}/g) ?? [])].sort();

describe("the AI connector screen's texts", () => {
  it("finds the keys it checks: the scan itself still works", () => {
    const keys = screenKeys();
    expect(keys.length).toBeGreaterThan(40);
    for (const key of ["AiConnector:State.Live", "AiConnector:State.SwitchedOff", "AiConnector:Status.NotAccepted", "AiConnector:Withdraw.Question", "AiConnector:SwitchOff.NQuestion.One", "AiConnector:SwitchedOffN.One", "AiConnector:Accept.DataMetrics", "Integrations:CopyValue", "Integrations:Nav.AiConnector"]) {
      expect(keys).toContain(key);
    }
  });

  it.each(["nl", "en"])("every key is in the %s resource, with a text", (lang) => {
    const texts = integrationsTexts(lang);
    const missing = screenKeys().filter((key) => !texts[key]?.trim());
    expect(missing).toEqual([]);
  });

  it("nl and en use the same placeholders", () => {
    const nl = integrationsTexts("nl");
    const en = integrationsTexts("en");
    for (const key of screenKeys()) {
      expect(placeholders(nl[key]), key).toEqual(placeholders(en[key]));
    }
  });

  it("the placeholders are the ones the screen fills", () => {
    const en = integrationsTexts("en");
    expect(placeholders(en["AiConnector:NoConnections"])).toEqual(["{0}"]);
    expect(placeholders(en["AiConnector:SwitchOff.Question"])).toEqual(["{0}", "{1}"]);
    expect(placeholders(en["AiConnector:Status.AcceptedBy"])).toEqual(["{0}", "{1}"]);
    expect(placeholders(en["AiConnector:Status.AcceptedOn"])).toEqual(["{0}"]);
    expect(placeholders(en["AiConnector:SwitchOffN"])).toEqual(["{0}"]);
    expect(placeholders(en["AiConnector:SwitchOff.NQuestion"])).toEqual(["{0}"]);
    expect(placeholders(en["AiConnector:SwitchedOffN"])).toEqual(["{0}"]);
    expect(placeholders(en["Integrations:CopyValue"])).toEqual(["{0}"]);
  });

  it.each(["nl", "en"])("the shared texts the screen's components use are in %s", (lang) => {
    const texts = { ...productTexts(lang), ...platformTexts(lang) };
    const missing = ["Cancel", "PermissionDenied", "UnexpectedError"].filter((key) => !texts[key]?.trim());
    expect(missing).toEqual([]);
  });

  it("Withdraw says in both languages that all connections stop", () => {
    expect(integrationsTexts("en")["AiConnector:Withdraw.Question"]).toMatch(/all connections stop/i);
    expect(integrationsTexts("nl")["AiConnector:Withdraw.Question"]).toMatch(/alle koppelingen stoppen/i);
  });

  it("the four states read as designed in English", () => {
    const en = integrationsTexts("en");
    expect(["Live", "NotWorking", "Expired", "SwitchedOff"].map((state) => en[`AiConnector:State.${state}`])).toEqual(["Live", "Not working", "Expired", "Switched off"]);
  });
});

describe("the status numbers", () => {
  it("are the backend enum's values", () => {
    const source = read(`${INTEGRATIONS}/AiConnector/AiConnectionStatus.cs`);
    const values = Object.fromEntries([...source.matchAll(/^\s*(\w+) = (\d+),?\s*$/gm)].map((match) => [match[1], Number(match[2])]));
    expect(values).toEqual({ Live: 1, NotWorking: 2, Expired: 3, SwitchedOff: 4 });
  });

  it("are the committed contract's values, and the rows carry them", () => {
    const schemas = (JSON.parse(read("openapi/opentms-next.json")) as { components: { schemas: Record<string, { enum?: number[]; properties?: Record<string, { $ref?: string }> }> } }).components.schemas;
    expect(schemas["OpenTms.Integrations.AiConnector.AiConnectionStatus"].enum).toEqual([1, 2, 3, 4]);
    expect(schemas["OpenTms.Integrations.AiConnector.AiConnectionRowDto"].properties!.status.$ref).toBe("#/components/schemas/OpenTms.Integrations.AiConnector.AiConnectionStatus");
  });
});
