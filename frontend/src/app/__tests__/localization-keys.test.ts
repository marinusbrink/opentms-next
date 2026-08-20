/// <reference types="node" />
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

import { describe, expect, it } from "vitest";

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
