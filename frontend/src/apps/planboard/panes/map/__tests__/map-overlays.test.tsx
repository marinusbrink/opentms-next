import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { MapLegend } from "@/apps/planboard/panes/map/MapLegend";
import { TripSummaryCard } from "@/apps/planboard/panes/map/TripSummaryCard";
import type { PlanboardTripMapDto } from "@/domains/planning-execution/planboard-map";

/* The map pane's overlays (design 70 "Map"), found in the browser check on the Large dataset (2026-11-01): in a short
 * Map pane the always-open legend pushed the trip summary card out of view. The legend is now folded to a button by
 * default; the card says when the trip has no calculated route yet (the map then draws straight lines).
 * Risk class: Low (presentation only). */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }) }));

const BASE_MAP: PlanboardTripMapDto = {
  id: "t1",
  number: "TR-2026-00001",
  status: 1,
  stops: [
    { id: "s1", position: 1, name: "Depot", city: "Uden", latitude: 51.66, longitude: 5.6, legIds: [] },
    { id: "s2", position: 2, name: "Customer", city: "Oss", latitude: 51.76, longitude: 5.52, legIds: [] },
  ],
  segments: [],
  routedAt: null,
} as PlanboardTripMapDto;

describe("MapLegend", () => {
  it("is folded by default and opens and closes from its button", () => {
    render(<MapLegend showAllTrips showUnplannedNear={false} />);
    const toggle = screen.getByRole("button", { name: /Planboard:Map.Legend/ });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByText("Planboard:Map.LegendRoute")).not.toBeInTheDocument();

    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByText("Planboard:Map.LegendRoute")).toBeInTheDocument();
    expect(screen.getByText("Planboard:Map.LegendAllTrips")).toBeInTheDocument();
    expect(screen.queryByText("Planboard:Map.LegendUnplanned")).not.toBeInTheDocument();

    fireEvent.click(toggle);
    expect(screen.queryByText("Planboard:Map.LegendRoute")).not.toBeInTheDocument();
  });
});

describe("TripSummaryCard", () => {
  it("says the route is not calculated yet when the trip has no route", () => {
    render(<TripSummaryCard map={BASE_MAP} />);
    expect(screen.getByText("Planboard:Map.NoRouteYet")).toBeInTheDocument();
  });

  it("does not show the hint once the trip is routed", () => {
    render(<TripSummaryCard map={{ ...BASE_MAP, routedAt: "2026-11-01T10:00:00Z", plannedDistanceM: 42_000 }} />);
    expect(screen.queryByText("Planboard:Map.NoRouteYet")).not.toBeInTheDocument();
  });

  it("does not show the hint for a trip with a single stop (nothing to route)", () => {
    render(<TripSummaryCard map={{ ...BASE_MAP, stops: BASE_MAP.stops!.slice(0, 1) }} />);
    expect(screen.queryByText("Planboard:Map.NoRouteYet")).not.toBeInTheDocument();
  });
});
