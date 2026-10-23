import { describe, expect, it, vi } from "vitest";

/* reportTemplateCommandBlock (design 50, UI design; UI-PERM-005): why a template command of the report definition
 * editor cannot run, as a localization key. The reasons have a fixed precedence: feature off, provider unavailable
 * (also when the provider failed to answer: the status carries a message), not configured, archived, the provider
 * does not support the command, template not found, not owned (Edit template only), and unsaved changes last.
 * Without a status (still loading, or the status query failed) only unsaved changes block. Risk class: Medium (UI
 * flow); the server enforces every one of these again on the command itself. */

vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));

import { reportTemplateCommandBlock, type ReportRenderingStatus, type ReportTemplateCommand } from "@/domains/platform/master-data";

const CLEAR: ReportRenderingStatus = {
  featureEnabled: true,
  providerAvailable: true,
  providerConfigured: true,
  supportsTemplateEditor: true,
  supportsTemplateCopy: true,
  templateFound: true,
  templateName: "Factuur NL",
  templateOwnedByWorkspace: true,
  message: null,
};

interface Condition {
  name: string;
  /** Which commands the condition blocks. */
  applies: ReportTemplateCommand[];
  reason: string;
  status?: (s: ReportRenderingStatus, command: ReportTemplateCommand) => ReportRenderingStatus;
  archived?: boolean;
  dirty?: boolean;
}

const ALL: ReportTemplateCommand[] = ["editTemplate", "copyTemplate", "testDocument"];

/* In precedence order: a condition wins over every condition after it. */
const CONDITIONS: Condition[] = [
  { name: "feature off", applies: ALL, reason: "Platform:RenderingFeatureOff", status: (s) => ({ ...s, featureEnabled: false }) },
  { name: "provider not available", applies: ALL, reason: "Platform:RenderingUnavailableHint", status: (s) => ({ ...s, providerAvailable: false }) },
  { name: "provider failed to answer (message)", applies: ALL, reason: "Platform:RenderingUnavailableHint", status: (s) => ({ ...s, message: "Timed out." }) },
  { name: "provider not configured", applies: ALL, reason: "OpenTms.Platform:RenderingNotConfigured", status: (s) => ({ ...s, providerConfigured: false }) },
  { name: "definition archived", applies: ALL, reason: "OpenTms.Platform:ReportDefinitionArchived", archived: true },
  { name: "template editor not supported", applies: ["editTemplate"], reason: "Platform:TemplateEditorNotSupported", status: (s) => ({ ...s, supportsTemplateEditor: false }) },
  { name: "template copy not supported", applies: ["copyTemplate"], reason: "Platform:TemplateCopyNotSupported", status: (s) => ({ ...s, supportsTemplateCopy: false }) },
  { name: "template not found", applies: ALL, reason: "Platform:TemplateNotFoundHint", status: (s) => ({ ...s, templateFound: false }) },
  { name: "template not owned", applies: ["editTemplate"], reason: "OpenTms.Platform:TemplateNotOwned", status: (s) => ({ ...s, templateOwnedByWorkspace: false }) },
  { name: "unsaved changes", applies: ALL, reason: "Platform:SaveChangesFirst", dirty: true },
];

/** Applies the conditions from `from` to the end: the expected reason is the first one that applies to the command. */
function apply(command: ReportTemplateCommand, from: number) {
  let status = { ...CLEAR };
  let archived = false;
  let dirty = false;
  for (const condition of CONDITIONS.slice(from)) {
    if (condition.status) status = condition.status(status, command);
    archived ||= condition.archived === true;
    dirty ||= condition.dirty === true;
  }
  return reportTemplateCommandBlock(command, status, archived, dirty);
}

describe("reportTemplateCommandBlock — every reason, in precedence order", () => {
  for (const command of ALL) {
    describe(command, () => {
      it("is not blocked when everything is in order", () => {
        expect(reportTemplateCommandBlock(command, CLEAR, false, false)).toBeUndefined();
        expect(reportTemplateCommandBlock(command, CLEAR, false)).toBeUndefined();
      });

      CONDITIONS.forEach((condition, index) => {
        if (!condition.applies.includes(command)) {
          it(`ignores "${condition.name}" on its own`, () => {
            let status = { ...CLEAR };
            if (condition.status) status = condition.status(status, command);
            expect(reportTemplateCommandBlock(command, status, condition.archived === true, condition.dirty === true)).toBeUndefined();
          });
          return;
        }
        it(`"${condition.name}" alone gives ${condition.reason}`, () => {
          let status = { ...CLEAR };
          if (condition.status) status = condition.status(status, command);
          expect(reportTemplateCommandBlock(command, status, condition.archived === true, condition.dirty === true)).toBe(condition.reason);
        });
        it(`"${condition.name}" wins over every later reason`, () => {
          expect(apply(command, index)).toBe(condition.reason);
        });
      });
    });
  }

  it("Edit template: not owned blocks, Copy template and Test document do not care (copying is the way out)", () => {
    const notOwned = { ...CLEAR, templateOwnedByWorkspace: false };
    expect(reportTemplateCommandBlock("editTemplate", notOwned, false)).toBe("OpenTms.Platform:TemplateNotOwned");
    expect(reportTemplateCommandBlock("copyTemplate", notOwned, false)).toBeUndefined();
    expect(reportTemplateCommandBlock("testDocument", notOwned, false)).toBeUndefined();
  });

  it("an unknown template (not looked up: null) blocks nothing", () => {
    const unknown = { ...CLEAR, templateFound: null, templateName: null, templateOwnedByWorkspace: null };
    for (const command of ALL) expect(reportTemplateCommandBlock(command, unknown, false)).toBeUndefined();
  });

  it("an empty message is not a provider failure", () => {
    for (const command of ALL) expect(reportTemplateCommandBlock(command, { ...CLEAR, message: "" }, false)).toBeUndefined();
  });

  it("a status the server sent empty ({}) reads as feature off", () => {
    for (const command of ALL) expect(reportTemplateCommandBlock(command, {}, false)).toBe("Platform:RenderingFeatureOff");
  });
});

describe("reportTemplateCommandBlock — without a status", () => {
  it.each(ALL)("%s: loading or failed status blocks nothing but unsaved changes", (command) => {
    expect(reportTemplateCommandBlock(command, undefined, false, false)).toBeUndefined();
    /* Archived alone is the status's to judge (the server refuses an archived definition and its error shows). */
    expect(reportTemplateCommandBlock(command, undefined, true, false)).toBeUndefined();
    expect(reportTemplateCommandBlock(command, undefined, false, true)).toBe("Platform:SaveChangesFirst");
    expect(reportTemplateCommandBlock(command, undefined, true, true)).toBe("Platform:SaveChangesFirst");
  });
});
