/// <reference types="node" />
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

import { describe, expect, it } from "vitest";

import { APPS } from "@/app/apps.config";

// Repo root is four levels up from frontend/src/app/__tests__/
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const REPO_ROOT = resolve((import.meta as any).dirname as string, "../../../..");

function readPlatformLocale(lang: string): { culture: string; texts: Record<string, string> } {
  const filePath = resolve(
    REPO_ROOT,
    `backend/modules/platform/OpenTms.Platform.Domain.Shared/Localization/Platform/${lang}.json`,
  );
  return JSON.parse(readFileSync(filePath, "utf-8"));
}

/** The product resource (app and shell names, the wizard texts): backend/src/OpenTms.Domain.Shared/Localization/OpenTms. */
function readOpenTmsLocale(lang: string): Record<string, string> {
  const filePath = resolve(REPO_ROOT, `backend/src/OpenTms.Domain.Shared/Localization/OpenTms/${lang}.json`);
  return (JSON.parse(readFileSync(filePath, "utf-8")) as { Texts: Record<string, string> }).Texts;
}

/** Every resource the frontend's t() can resolve a key from: the product resource and every module's. */
function readAllTexts(lang: string): Record<string, string> {
  const modules = ["financial", "integrations", "master-data", "orders", "planning-execution", "platform", "reporting"];
  const names: Record<string, string> = { "master-data": "MasterData", "planning-execution": "PlanningExecution" };
  const texts: Record<string, string> = { ...readOpenTmsLocale(lang) };
  for (const module of modules) {
    const name = names[module] ?? module[0].toUpperCase() + module.slice(1);
    const filePath = resolve(REPO_ROOT, `backend/modules/${module}/OpenTms.${name}.Domain.Shared/Localization/${name}/${lang}.json`);
    const parsed = JSON.parse(readFileSync(filePath, "utf-8")) as { texts?: Record<string, string>; Texts?: Record<string, string> };
    Object.assign(texts, parsed.texts ?? parsed.Texts ?? {});
  }
  return texts;
}

describe("Platform localization key additions (low risk)", () => {
  describe("en.json", () => {
    const { texts } = readPlatformLocale("en");

    it("has Shell:NavCollapse key with non-empty value", () => {
      expect(texts["Shell:NavCollapse"]).toBeDefined();
      expect(texts["Shell:NavCollapse"].trim().length).toBeGreaterThan(0);
    });

    it("has Shell:NavExpand key with non-empty value", () => {
      expect(texts["Shell:NavExpand"]).toBeDefined();
      expect(texts["Shell:NavExpand"].trim().length).toBeGreaterThan(0);
    });

    it("Shell:NavCollapse and Shell:NavExpand are distinct strings", () => {
      expect(texts["Shell:NavCollapse"]).not.toBe(texts["Shell:NavExpand"]);
    });
  });

  describe("nl.json", () => {
    const { texts } = readPlatformLocale("nl");

    it("has Shell:NavCollapse key with non-empty value", () => {
      expect(texts["Shell:NavCollapse"]).toBeDefined();
      expect(texts["Shell:NavCollapse"].trim().length).toBeGreaterThan(0);
    });

    it("has Shell:NavExpand key with non-empty value", () => {
      expect(texts["Shell:NavExpand"]).toBeDefined();
      expect(texts["Shell:NavExpand"].trim().length).toBeGreaterThan(0);
    });

    it("Shell:NavCollapse and Shell:NavExpand are distinct strings", () => {
      expect(texts["Shell:NavCollapse"]).not.toBe(texts["Shell:NavExpand"]);
    });
  });
});

describe("Platform localization key additions — command bar and role bulk delete (low risk)", () => {
  const KEYS = [
    "Administration:NSelected",
    "Administration:ConfirmBulkDeleteRoles",
    "Permission:Administration.Roles.BulkDelete",
  ];

  for (const lang of ["en", "nl"]) {
    describe(`${lang}.json`, () => {
      const { texts } = readPlatformLocale(lang);

      for (const key of KEYS) {
        it(`has '${key}' with non-empty value`, () => {
          expect(texts[key]).toBeDefined();
          expect(texts[key].trim().length).toBeGreaterThan(0);
        });
      }

      it("Administration:NSelected contains the {0} placeholder", () => {
        expect(texts["Administration:NSelected"]).toContain("{0}");
      });

      it("Administration:ConfirmBulkDeleteRoles contains the {0} placeholder", () => {
        expect(texts["Administration:ConfirmBulkDeleteRoles"]).toContain("{0}");
      });
    });
  }

  it("en and nl Administration:NSelected are distinct (not copy-paste)", () => {
    const en = readPlatformLocale("en").texts;
    const nl = readPlatformLocale("nl").texts;
    expect(en["Administration:NSelected"]).not.toBe(nl["Administration:NSelected"]);
  });

  it("en and nl Administration:ConfirmBulkDeleteRoles are distinct (not copy-paste)", () => {
    const en = readPlatformLocale("en").texts;
    const nl = readPlatformLocale("nl").texts;
    expect(en["Administration:ConfirmBulkDeleteRoles"]).not.toBe(
      nl["Administration:ConfirmBulkDeleteRoles"],
    );
  });
});

describe("OpenTms resource additions — the Transport app's nav names and the wizard texts (design 61; low risk)", () => {
  const NAV_KEYS = [
    "App:Transport.Orders",
    "App:Transport.Templates",
    "App:Transport.Trips",
    "App:Transport.Documents",
    "App:Transport.View.Today",
    "App:Transport.View.Tomorrow",
    "App:Transport.View.ThisWeek",
    "App:Transport.View.Open",
    "App:Transport.View.Drafts",
    "App:Transport.View.All",
  ];
  const LIBRARY_KEYS = [
    "RecordNotFound",
    "Wizard:Previous",
    "Wizard:Next",
    "Wizard:StepOf",
    "Wizard:Steps",
    "Wizard:Progress",
    "Wizard:Task.Waiting",
    "Wizard:Task.Running",
    "Wizard:Task.Done",
    "Wizard:Task.Failed",
  ];

  for (const lang of ["en", "nl"]) {
    it(`${lang}.json has every key with a non-empty value and no em dash (UI-FB-007)`, () => {
      const texts = readOpenTmsLocale(lang);
      for (const key of [...NAV_KEYS, ...LIBRARY_KEYS]) {
        expect(texts[key], key).toBeDefined();
        expect(texts[key].trim().length, key).toBeGreaterThan(0);
        expect(texts[key], key).not.toContain("—");
      }
      expect(texts["Wizard:StepOf"]).toContain("{0}");
      expect(texts["Wizard:StepOf"]).toContain("{1}");
      expect(texts["Wizard:Progress"]).toContain("{0}");
      expect(texts["Wizard:Progress"]).toContain("{1}");
    });
  }

  it("the Dutch nav names are the ones the PO gave", () => {
    const nl = readOpenTmsLocale("nl");
    expect([nl["App:Transport.Orders"], nl["App:Transport.Templates"], nl["App:Transport.Trips"], nl["App:Transport.Documents"]]).toEqual(["Opdrachten", "Sjablonen", "Ritten", "Documenten"]);
    expect(NAV_KEYS.filter((k) => k.startsWith("App:Transport.View.")).map((k) => nl[k])).toEqual(["Vandaag", "Morgen", "Deze week", "Open", "Concepten", "Alles"]);
  });
});

describe("apps.config nameKeys resolve in a localization resource (nl and en)", () => {
  /* Keys whose texts arrive with another builder's backend module (design 61: the Orders, Planning & Execution and
   * Financial resources are theirs). Remove an entry here when its module lands; the test then demands the text. */
  const PENDING: Record<string, string> = {
    "Orders:ConsignmentNoteProfiles": "TODO: Orders module (design 58)",
    "Orders:ConsignmentNoteFixedDescriptions": "TODO: Orders module (design 58)",
    "Orders:OrderAttributeDefinitions": "TODO: Orders module (design 58)",
  };
  /* StatusBadge label keys registered in design 61; the texts arrive with the modules too. */
  const PENDING_STATUS_KEYS = [
    ...["Draft", "Open", "ReadyForInvoicing", "Invoiced", "Archived", "Cancelled"].map((s) => `Orders:Status.${s}`),
    ...["Planned", "InProgress", "Completed", "Cancelled"].map((s) => `PlanningExecution:Status.${s}`),
  ];

  const nameKeys = (): string[] => {
    const keys: string[] = [];
    const walk = (views: readonly { nameKey: string; children?: readonly unknown[] }[] | undefined) =>
      views?.forEach((view) => {
        keys.push(view.nameKey);
        walk(view.children as typeof views);
      });
    for (const app of APPS) {
      keys.push(app.nameKey);
      walk(app.views);
    }
    return keys;
  };

  for (const lang of ["en", "nl"]) {
    it(`${lang}: every app and nav name has a text, except the allow-listed pending ones`, () => {
      const texts = readAllTexts(lang);
      const missing = nameKeys().filter((key) => !(key in PENDING) && !texts[key]?.trim());
      expect(missing).toEqual([]);
    });

    it(`${lang}: the allow-list names only keys that are still missing (drop an entry once its module lands)`, () => {
      const texts = readAllTexts(lang);
      const landed = [...Object.keys(PENDING), ...PENDING_STATUS_KEYS].filter((key) => texts[key]?.trim());
      expect(landed).toEqual([]);
    });
  }

  it("every pending key is used by apps.config or the StatusBadge registry", () => {
    const used = new Set(nameKeys());
    for (const key of Object.keys(PENDING)) expect(used.has(key), key).toBe(true);
  });
});

describe("Platform localization key additions — grid settings failure toasts (low risk)", () => {
  const KEYS = ["Grid:SettingsSaveFailed", "Grid:SettingsResetFailed"];

  for (const lang of ["en", "nl"]) {
    it(`${lang}.json has every key with a non-empty value`, () => {
      const { texts } = readPlatformLocale(lang);
      for (const key of KEYS) {
        expect(texts[key], key).toBeDefined();
        expect(texts[key].trim().length, key).toBeGreaterThan(0);
      }
    });
  }
});
