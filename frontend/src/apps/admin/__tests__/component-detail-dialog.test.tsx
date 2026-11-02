import { render, screen } from "@testing-library/react";
import { beforeAll, describe, expect, it, vi } from "vitest";

/* The component dialog of the Health tab (docs/designs/maintenance-view.md, "Components"). Risk class: High — it is
 * what an administrator acts on during an incident: the finding with what to do, what depends on the component, the
 * settings by name with their environment-variable form (never a value), and the check's own error. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({
    t: (k: string) =>
      ({
        "HealthCheck:pdfgeneratorapi": "Documents",
        "HealthCheck:pdfgeneratorapi:Impact": "No document can be generated.",
        "HealthCheck:pdfgeneratorapi:Failing": "pdfgeneratorapi refuses the request.",
      })[k] ?? k,
    culture: "en",
    isLoading: false,
  }),
}));

const { ComponentDetailDialog } = await import("@/apps/admin/components/ComponentDetailDialog");

beforeAll(() => {
  vi.stubGlobal("ResizeObserver", class { observe() {} disconnect() {} unobserve() {} });
});

describe("ComponentDetailDialog", () => {
  it("shows the finding, the impact, the settings with their environment variables and the error", () => {
    render(
      <ComponentDetailDialog
        open
        onClose={() => {}}
        entry={{
          name: "pdfgeneratorapi",
          status: "Unhealthy",
          area: "External",
          reason: "Failing",
          isOptional: false,
          target: "us1.pdfgeneratorapi.com",
          settings: ["Integrations:PdfGeneratorApi:Key", "Integrations:PdfGeneratorApi:Secret"],
          description: "pdfgeneratorapi: HTTP 401: Unauthorized",
          error: null,
          durationMilliseconds: 120,
          tags: ["external"],
          data: { status: "401" },
        }}
      />,
    );

    expect(screen.getByText("Maintenance:Component Documents")).toBeInTheDocument();
    expect(screen.getByText("pdfgeneratorapi refuses the request.")).toBeInTheDocument();
    expect(screen.getByText("No document can be generated.")).toBeInTheDocument();
    expect(screen.getByText("us1.pdfgeneratorapi.com")).toBeInTheDocument();
    expect(screen.getByText("Integrations:PdfGeneratorApi:Key")).toBeInTheDocument();
    expect(screen.getByText("Integrations__PdfGeneratorApi__Key")).toBeInTheDocument();
    expect(screen.getByText("pdfgeneratorapi: HTTP 401: Unauthorized")).toBeInTheDocument();
    expect(screen.getByText("Maintenance:Area.External")).toBeInTheDocument();
  });

  it("marks an optional component and shows a check's error", () => {
    render(
      <ComponentDetailDialog
        open
        onClose={() => {}}
        entry={{ name: "live-updates", status: "Degraded", area: "Core", isOptional: true, settings: [], error: "No connection", tags: ["core"] }}
      />,
    );

    expect(screen.getByText("Maintenance:Area.Core · Maintenance:Optional")).toBeInTheDocument();
    expect(screen.getByText("No connection")).toBeInTheDocument();
    expect(screen.queryByText("Maintenance:Settings")).toBeNull();
  });
});
