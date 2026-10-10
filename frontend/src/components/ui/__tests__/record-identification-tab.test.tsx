import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { RecordIdentificationTab, recordIdentificationTab } from "@/components/ui/record-identification-tab";

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (key: string) => key, culture: "nl", isLoading: false }),
}));

describe("RecordIdentificationTab (UI-FORM-008 standard tab)", () => {
  it("shows the id and the technical creation timestamp", () => {
    render(<RecordIdentificationTab id="3f2a" creationTime={new Date(2026, 9, 9, 14, 30, 15).toISOString()} />);
    expect(screen.getByText("3f2a")).toBeInTheDocument();
    expect(screen.getByText("2026-10-09 14:30:15")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Record:CopyId" })).toBeInTheDocument();
  });

  it("explains that a new record has no identification yet", () => {
    render(<RecordIdentificationTab />);
    expect(screen.getByText("Record:NotSavedYet")).toBeInTheDocument();
  });

  it("builds the dialog tab with the fingerprint icon and the identification value", () => {
    const tab = recordIdentificationTab((k) => k, { id: "x" });
    expect(tab.value).toBe("identification");
    expect(tab.label).toBe("RecordIdentification");
    expect(tab.icon).toBeDefined();
  });
});
