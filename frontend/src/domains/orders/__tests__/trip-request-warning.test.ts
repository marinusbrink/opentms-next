import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/api/client", () => ({ apiClient: { GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } }));

import { tripRequestWarningText } from "@/domains/orders/transport-orders";

/* The trip request warning text (design 58 build note 19): the localized error code with its {field}. Risk class: Low. */

const texts: Record<string, string> = { "OpenTms.Orders:TripRequestNotHonoured": "The trip of this order was not created: {field} is not known to Planning." };
const t = (key: string) => texts[key] ?? key;

describe("tripRequestWarningText", () => {
  it("localizes the code and fills in the field", () => {
    expect(tripRequestWarningText(t, { warningCode: "OpenTms.Orders:TripRequestNotHonoured", warningField: "CarrierRelation" })).toBe("The trip of this order was not created: CarrierRelation is not known to Planning.");
  });

  it("is null without a warning", () => {
    expect(tripRequestWarningText(t, { warningCode: null, warningField: "x" })).toBeNull();
    expect(tripRequestWarningText(t, undefined)).toBeNull();
  });
});
