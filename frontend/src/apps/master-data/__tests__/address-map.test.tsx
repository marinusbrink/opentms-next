import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

/* AddressMap (design 43, PO 2026-10-12): the OpenStreetMap embed around the coordinates of an
 * address, or the hint to enter them. addressMapUrl builds the embed URL: a bbox of ±0.01 degree
 * around the point and a marker on it, five decimals, longitude first in the bbox and latitude
 * first in the marker (the embed's own order).
 * Risk class: Low (static presentation of coordinates) — unit tests. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

import { AddressMap, addressMapUrl } from "@/domains/master-data/components/AddressMap";

const frame = () => screen.queryByTitle("MasterData:Map") as HTMLIFrameElement | null;
const hint = () => screen.queryByText("MasterData:MapNoCoordinates");

describe("addressMapUrl", () => {
  it("puts a ±0.01 degree bbox (west,south,east,north) and the marker (lat,lon) in the OpenStreetMap embed URL", () => {
    expect(addressMapUrl(52.01, 4.36)).toBe(
      "https://www.openstreetmap.org/export/embed.html?bbox=4.35000,52.00000,4.37000,52.02000&layer=mapnik&marker=52.01000,4.36000",
    );
  });

  it("keeps the sign and five decimals for southern and western coordinates", () => {
    const url = new URL(addressMapUrl(-33.8688, -70.6));
    expect(url.origin).toBe("https://www.openstreetmap.org");
    expect(url.pathname).toBe("/export/embed.html");
    expect(url.searchParams.get("bbox")).toBe("-70.61000,-33.87880,-70.59000,-33.85880");
    expect(url.searchParams.get("marker")).toBe("-33.86880,-70.60000");
    expect(url.searchParams.get("layer")).toBe("mapnik");
  });

  it("rounds, not truncates, beyond five decimals", () => {
    expect(addressMapUrl(52.123456, 4.987654)).toContain("marker=52.12346,4.98765");
  });
});

describe("AddressMap", () => {
  it("renders the embed as a lazy, referrer-free iframe titled Map when both coordinates are valid", () => {
    render(<AddressMap latitude={52.01} longitude={4.36} className="extra" />);
    const iframe = frame();
    expect(iframe).not.toBeNull();
    expect(iframe!.src).toBe(addressMapUrl(52.01, 4.36));
    expect(iframe!.getAttribute("loading")).toBe("lazy");
    expect(iframe!.getAttribute("referrerpolicy")).toBe("no-referrer");
    expect(hint()).toBeNull();
    const container = document.querySelector("[data-slot=address-map]");
    expect(container).not.toBeNull();
    expect(container!.className).toContain("extra");
  });

  it("accepts the edges of the valid range", () => {
    render(<AddressMap latitude={-90} longitude={180} />);
    expect(frame()!.src).toContain("marker=-90.00000,180.00000");
  });

  it.each([
    ["both missing", null, null],
    ["latitude missing", null, 4.36],
    ["longitude missing", 52.01, null],
    ["latitude not a number", Number.NaN, 4.36],
    ["longitude not a number", 52.01, Number.NaN],
    ["latitude infinite", Number.POSITIVE_INFINITY, 4.36],
    ["latitude above 90", 90.01, 4.36],
    ["latitude below -90", -91, 4.36],
    ["longitude above 180", 52.01, 180.5],
    ["longitude below -180", 52.01, -181],
  ])("shows the no-coordinates hint instead of a map when %s", (_case, latitude, longitude) => {
    render(<AddressMap latitude={latitude} longitude={longitude} />);
    expect(frame()).toBeNull();
    expect(hint()).toBeInTheDocument();
    expect(document.querySelector("[data-slot=address-map]")).not.toBeNull();
  });
});
