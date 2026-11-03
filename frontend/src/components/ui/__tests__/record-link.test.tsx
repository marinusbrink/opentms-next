import { render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/* RecordLink (design 72: the trace's links to the intake and the order, a profile's link to its source document): a
 * router link (so Back returns where the user came from), never a plain href; the brand colour (UI-TOK-002) and the
 * caller's classes; the route and search passed on as given. The router's Link is captured to see what it receives.
 * Risk class: Low. */

const received: Array<Record<string, unknown>> = [];
vi.mock("@tanstack/react-router", () => ({
  Link: (props: { to: string; search?: Record<string, string>; className?: string; children?: ReactNode }) => {
    received.push(props);
    return (
      <a href={props.to} className={props.className} data-router-link="">
        {props.children}
      </a>
    );
  },
}));

import { RecordLink } from "@/components/ui/record-link";

beforeEach(() => {
  received.length = 0;
});

describe("RecordLink", () => {
  it("renders its children through the router's Link with the route", () => {
    render(<RecordLink to="/transport/intake/i-1">Intake</RecordLink>);

    const link = screen.getByRole("link", { name: "Intake" });
    expect(link).toHaveAttribute("data-router-link");
    expect(received).toHaveLength(1);
    expect(received[0].to).toBe("/transport/intake/i-1");
    expect(received[0].search).toBeUndefined();
  });

  it("passes the search on as given", () => {
    render(
      <RecordLink to="/integrations/received/m-1" search={{ document: "d-2" }}>
        Trace
      </RecordLink>,
    );

    expect(received[0].search).toEqual({ document: "d-2" });
  });

  it("carries the brand colour and merges the caller's classes", () => {
    render(
      <RecordLink to="/transport/orders/o-1" className="font-medium">
        TO-1
      </RecordLink>,
    );

    const className = screen.getByRole("link", { name: "TO-1" }).className;
    expect(className).toContain("text-brand");
    expect(className).toContain("hover:underline");
    expect(className).toContain("font-medium");
  });
});
