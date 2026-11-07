import { describe, expect, it } from "vitest";

import type { RelationOption } from "@/domains/master-data/entities";
import {
  addLeg,
  canAddLeg,
  canRemoveLeg,
  chainErrors,
  chainRowsOf,
  chainUpdateOf,
  isPlaceLocked,
  removeLeg,
  setAddress,
  setEndField,
  setRelation,
  setRemarks,
  splitLocal,
  type ChainEnd,
  type ChainRow,
  type SuccessorOf,
} from "@/domains/planning-execution/components/manage-legs-model";
import type { LegChainDto } from "@/domains/planning-execution/legs";

/* The pure part of Manage legs (design 74 §4): add, remove, auto-chaining, the refusals around read-only legs, and the body
 * of the save. The server validates the chain; these functions keep the form consistent as the planner edits it. Risk class:
 * High (the order chain and planning). The server side of the contract is in the backend's PlanboardReviewManageLegsTests. */

const relation = (id: string): RelationOption => ({ id, code: id, name: `Relation ${id}`, city: null, countryCode: null, roles: 0 });

const end = (id: string | null, patch: Partial<ChainEnd> = {}): ChainEnd => ({
  waypointTypeId: "load",
  relation: id ? relation(id) : null,
  relationAddressId: id ? `addr-${id}` : null,
  plannedDate: "2026-11-10",
  windowFromDate: null,
  windowFromTime: "",
  windowTillDate: null,
  windowTillTime: "",
  reference: "",
  ...patch,
});

const row = (key: string, origin: string | null, destination: string | null, patch: Partial<ChainRow> = {}): ChainRow => ({
  key,
  id: key,
  readOnly: false,
  tripNumber: null,
  hasActuals: false,
  remarks: "",
  origin: end(origin),
  destination: end(destination),
  distanceM: null,
  durationS: null,
  ...patch,
});

/** A→B→C→D as three legs. */
const chain = () => [row("l1", "A", "B"), row("l2", "B", "C"), row("l3", "C", "D")];

const successorOf: SuccessorOf = (id) => (id === "load" ? "unload" : id === "via" ? null : null);

describe("isPlaceLocked", () => {
  it("locks the first row's origin and the last row's destination", () => {
    const rows = chain();
    expect(isPlaceLocked(rows, 0, "origin")).toBe(true);
    expect(isPlaceLocked(rows, 2, "destination")).toBe(true);
  });

  it("leaves the joints free", () => {
    const rows = chain();
    expect(isPlaceLocked(rows, 0, "destination")).toBe(false);
    expect(isPlaceLocked(rows, 1, "origin")).toBe(false);
    expect(isPlaceLocked(rows, 1, "destination")).toBe(false);
    expect(isPlaceLocked(rows, 2, "origin")).toBe(false);
  });

  it("locks both ends of a read-only row and a neighbour's end that must equal one of its ends", () => {
    const rows = [row("l1", "A", "B"), row("l2", "B", "C", { readOnly: true }), row("l3", "C", "D")];
    expect(isPlaceLocked(rows, 1, "origin")).toBe(true);
    expect(isPlaceLocked(rows, 1, "destination")).toBe(true);
    expect(isPlaceLocked(rows, 0, "destination")).toBe(true);
    expect(isPlaceLocked(rows, 2, "origin")).toBe(true);
  });
});

describe("addLeg", () => {
  it("appends a row ending where the last row ended, starting with the successor activity and no relation", () => {
    const rows = [row("l1", "A", "B", { destination: end("B", { reference: "REF", windowFromDate: "2026-11-10", windowFromTime: "08:00", windowTillDate: "2026-11-10", windowTillTime: "10:00" }) })];
    const next = addLeg(rows, successorOf, "new-1");
    expect(next).toHaveLength(2);
    const added = next[1];
    expect(added).toMatchObject({ key: "new-1", id: null, readOnly: false, tripNumber: null, hasActuals: false, remarks: "", distanceM: null, durationS: null });
    expect(added.origin).toEqual(end(null, { waypointTypeId: "unload" }));
    expect(added.destination).toEqual(rows[0].destination);
  });

  it("clears the old last row's destination until a relation is chosen, keeping its activity and date", () => {
    const rows = [row("l1", "A", "B", { destination: end("B", { reference: "REF", windowFromDate: "2026-11-11", windowFromTime: "08:00", windowTillDate: "2026-11-11", windowTillTime: "10:00", plannedDate: "2026-11-11" }) })];
    const [old] = addLeg(rows, successorOf, "new-1");
    expect(old.destination).toEqual(end(null, { plannedDate: "2026-11-11" }));
    expect(old.origin).toEqual(rows[0].origin);
  });

  it("an activity without a successor follows itself", () => {
    const rows = [row("l1", "A", "B", { destination: end("B", { waypointTypeId: "via" }) })];
    expect(addLeg(rows, successorOf, "n")[1].origin.waypointTypeId).toBe("via");
  });

  it("does not change the rows it was given", () => {
    const rows = chain();
    const snapshot = JSON.stringify(rows);
    addLeg(rows, successorOf, "n");
    expect(JSON.stringify(rows)).toBe(snapshot);
  });

  it("is refused after a read-only last row: it would change that leg's end", () => {
    const rows = [row("l1", "A", "B"), row("l2", "B", "C", { readOnly: true })];
    expect(canAddLeg(rows)).toBe(false);
    expect(addLeg(rows, successorOf, "n")).toEqual(rows);
  });

  it("is refused on nothing, allowed after a read-only row that is not the last", () => {
    expect(canAddLeg([])).toBe(false);
    expect(canAddLeg([row("l1", "A", "B", { readOnly: true }), row("l2", "B", "C")])).toBe(true);
  });
});

describe("removeLeg", () => {
  it("first: the next row takes its origin", () => {
    const next = removeLeg(chain(), 0);
    expect(next.map((r) => r.key)).toEqual(["l2", "l3"]);
    expect(next[0].origin.relation?.id).toBe("A");
    expect(next[0].destination.relation?.id).toBe("C");
  });

  it("middle: the next row takes its origin", () => {
    const next = removeLeg(chain(), 1);
    expect(next.map((r) => r.key)).toEqual(["l1", "l3"]);
    expect(next[1].origin.relation?.id).toBe("B");
    expect(next[0].destination.relation?.id).toBe("B");
  });

  it("last: the previous row takes its destination", () => {
    const next = removeLeg(chain(), 2);
    expect(next.map((r) => r.key)).toEqual(["l1", "l2"]);
    expect(next[1].destination.relation?.id).toBe("D");
  });

  it("keeps the chain contiguous at every removal", () => {
    for (const index of [0, 1, 2]) {
      const next = removeLeg(chain(), index);
      expect(next[0].origin.relation?.id).toBe("A");
      expect(next[1].destination.relation?.id).toBe("D");
      expect(next[1].origin.relation?.id).toBe(next[0].destination.relation?.id);
    }
  });

  it("keeps at least one row", () => {
    const rows = [row("l1", "A", "B")];
    expect(canRemoveLeg(rows, 0)).toBe(false);
    expect(removeLeg(rows, 0)).toEqual(rows);
  });

  it("refuses a read-only row and an index that is not there", () => {
    const rows = [row("l1", "A", "B", { readOnly: true }), row("l2", "B", "C"), row("l3", "C", "D")];
    expect(canRemoveLeg(rows, 0)).toBe(false);
    expect(canRemoveLeg(rows, 9)).toBe(false);
    expect(removeLeg(rows, 0)).toEqual(rows);
  });

  it("refuses when the row that would take over the end is read-only", () => {
    const nextReadOnly = [row("l1", "A", "B"), row("l2", "B", "C", { readOnly: true })];
    expect(canRemoveLeg(nextReadOnly, 0)).toBe(false);
    const previousReadOnly = [row("l1", "A", "B", { readOnly: true }), row("l2", "B", "C")];
    expect(canRemoveLeg(previousReadOnly, 1)).toBe(false);
    expect(removeLeg(previousReadOnly, 1)).toEqual(previousReadOnly);
  });

  it("allows a middle row after a read-only one: the next row takes the end the read-only leg already has", () => {
    const rows = [row("l1", "A", "B", { readOnly: true }), row("l2", "B", "C"), row("l3", "C", "D")];
    expect(canRemoveLeg(rows, 1)).toBe(true);
    const next = removeLeg(rows, 1);
    expect(next[0]).toEqual(rows[0]);
    expect(next[1].origin.relation?.id).toBe("B");
  });
});

describe("setRelation: auto-chaining", () => {
  it("a destination relation sets the next row's origin: relation, empty address, date and successor activity", () => {
    const rows = [row("l1", "A", "B", { destination: end("B", { plannedDate: "2026-11-12" }) }), row("l2", "B", "C")];
    const next = setRelation(rows, 0, "destination", relation("X"), successorOf);
    expect(next[0].destination).toMatchObject({ relation: relation("X"), relationAddressId: null });
    expect(next[1].origin).toMatchObject({ relation: relation("X"), relationAddressId: null, plannedDate: "2026-11-12", waypointTypeId: "unload" });
    expect(next[1].destination).toEqual(rows[1].destination);
  });

  it("the next row keeps its own date when the destination has none", () => {
    const rows = [row("l1", "A", "B", { destination: end("B", { plannedDate: null }) }), row("l2", "B", "C")];
    expect(setRelation(rows, 0, "destination", relation("X"), successorOf)[1].origin.plannedDate).toBe("2026-11-10");
  });

  it("without a successor the activity itself follows; without either the next row keeps its own", () => {
    const via = [row("l1", "A", "B", { destination: end("B", { waypointTypeId: "via" }) }), row("l2", "B", "C", { origin: end("B", { waypointTypeId: "own" }) })];
    expect(setRelation(via, 0, "destination", relation("X"), successorOf)[1].origin.waypointTypeId).toBe("via");
    const none = [row("l1", "A", "B", { destination: end("B", { waypointTypeId: null }) }), row("l2", "B", "C", { origin: end("B", { waypointTypeId: "own" }) })];
    expect(setRelation(none, 0, "destination", relation("X"), successorOf)[1].origin.waypointTypeId).toBe("own");
  });

  it("an origin relation sets the previous row's destination, relation and empty address only", () => {
    const rows = [row("l1", "A", "B", { destination: end("B", { waypointTypeId: "unload", plannedDate: "2026-11-09" }) }), row("l2", "B", "C")];
    const next = setRelation(rows, 1, "origin", relation("X"), successorOf);
    expect(next[1].origin).toMatchObject({ relation: relation("X"), relationAddressId: null });
    expect(next[0].destination).toMatchObject({ relation: relation("X"), relationAddressId: null, waypointTypeId: "unload", plannedDate: "2026-11-09" });
    expect(next[0].origin).toEqual(rows[0].origin);
  });

  it("clearing a relation clears the neighbour's too", () => {
    const next = setRelation(chain(), 0, "destination", null, successorOf);
    expect(next[0].destination.relation).toBeNull();
    expect(next[1].origin.relation).toBeNull();
  });

  it("a read-only neighbour does not follow", () => {
    const after = [row("l1", "A", "B"), row("l2", "B", "C", { readOnly: true })];
    expect(setRelation(after, 0, "destination", relation("X"), successorOf)[1]).toEqual(after[1]);
    const before = [row("l1", "A", "B", { readOnly: true }), row("l2", "B", "C")];
    expect(setRelation(before, 1, "origin", relation("X"), successorOf)[0]).toEqual(before[0]);
  });

  it("the ends of the chain have no neighbour; an index that is not there changes nothing", () => {
    const rows = chain();
    expect(setRelation(rows, 2, "destination", relation("X"), successorOf)[2].destination.relation?.id).toBe("X");
    expect(setRelation(rows, 0, "origin", relation("X"), successorOf)[0].origin.relation?.id).toBe("X");
    expect(setRelation(rows, 9, "origin", relation("X"), successorOf)).toEqual(rows);
  });
});

describe("setAddress: the same place on both sides of a joint", () => {
  it("mirrors a destination's address on the next origin and an origin's on the previous destination", () => {
    const down = setAddress(chain(), 0, "destination", "new-addr");
    expect(down[0].destination.relationAddressId).toBe("new-addr");
    expect(down[1].origin.relationAddressId).toBe("new-addr");
    const up = setAddress(chain(), 1, "origin", "new-addr");
    expect(up[1].origin.relationAddressId).toBe("new-addr");
    expect(up[0].destination.relationAddressId).toBe("new-addr");
  });

  it("a read-only neighbour and the chain's ends have nothing to mirror", () => {
    const rows = [row("l1", "A", "B"), row("l2", "B", "C", { readOnly: true })];
    expect(setAddress(rows, 0, "destination", "new-addr")[1]).toEqual(rows[1]);
    const ends = chain();
    expect(setAddress(ends, 2, "destination", "x")[2].destination.relationAddressId).toBe("x");
    expect(setAddress(ends, 0, "origin", "x")[0].origin.relationAddressId).toBe("x");
    expect(setAddress(ends, 9, "origin", "x")).toEqual(ends);
  });
});

describe("setEndField and setRemarks", () => {
  it("patch only the addressed row and side", () => {
    const rows = chain();
    const next = setEndField(rows, 1, "origin", { reference: "R" });
    expect(next[1].origin.reference).toBe("R");
    expect(next[0]).toEqual(rows[0]);
    expect(next[0].destination.reference).toBe("");
    expect(setEndField(rows, 9, "origin", { reference: "R" })).toEqual(rows);
    expect(setRemarks(rows, 2, "note")[2].remarks).toBe("note");
  });
});

describe("chainErrors", () => {
  it("is empty for a complete chain", () => {
    expect(chainErrors(chain()).size).toBe(0);
  });

  it("names what is missing per row and side", () => {
    const rows = [row("l1", "A", "B", { origin: end("A", { waypointTypeId: null }), destination: end(null, { plannedDate: null }) })];
    expect([...chainErrors(rows)].sort()).toEqual(["l1.destination.plannedDate", "l1.destination.relation", "l1.origin.waypointTypeId"]);
  });

  it("names an unreadable window time and accepts an empty or valid one", () => {
    const rows = [row("l1", "A", "B", { origin: end("A", { windowFromTime: "25:00", windowTillTime: "nope" }), destination: end("B", { windowFromTime: " ", windowTillTime: "0930" }) })];
    expect([...chainErrors(rows)].sort()).toEqual(["l1.origin.windowFromTime", "l1.origin.windowTillTime"]);
  });

  it("flags a freshly added row until its origin relation is chosen", () => {
    const next = addLeg([row("l1", "A", "B")], successorOf, "new-1");
    expect([...chainErrors(next)].sort()).toEqual(["l1.destination.relation", "new-1.origin.relation"]);
  });
});

const dto = (): LegChainDto =>
  ({
    orderLineId: "line-1",
    isChain: true,
    rows: [
      { isReadOnly: false, hasActuals: false, leg: { id: "l1", concurrencyStamp: "s1", remarks: "r", distanceM: 1000, durationS: 60, tripNumber: null, origin: { waypointTypeId: "load", relationId: "A", name: "Rel A", city: "Tilburg", countryCode: "NL", relationAddressId: "a", plannedDate: "2026-11-10", windowFrom: new Date(2026, 10, 10, 8, 0).toISOString(), windowTill: new Date(2026, 10, 10, 10, 30).toISOString(), reference: "O" }, destination: { waypointTypeId: "unload", relationId: "B", name: "Rel B" } } },
      { isReadOnly: true, hasActuals: true, leg: { id: "l2", concurrencyStamp: "s2", tripNumber: "TR-1", origin: { relationId: "B" }, destination: {} } },
    ],
  }) as unknown as LegChainDto;

describe("chainRowsOf", () => {
  it("maps the server's rows, with the flags and the windows split in local time", () => {
    const [first, second] = chainRowsOf(dto());
    expect(first).toMatchObject({ key: "l1", id: "l1", readOnly: false, hasActuals: false, tripNumber: null, remarks: "r", distanceM: 1000, durationS: 60 });
    expect(first.origin).toEqual({ waypointTypeId: "load", relation: { id: "A", code: "", name: "Rel A", city: "Tilburg", countryCode: "NL", roles: 0 }, relationAddressId: "a", plannedDate: "2026-11-10", windowFromDate: "2026-11-10", windowFromTime: "08:00", windowTillDate: "2026-11-10", windowTillTime: "10:30", reference: "O" });
    expect(second).toMatchObject({ readOnly: true, hasActuals: true, tripNumber: "TR-1" });
    expect(second.destination.relation).toBeNull();
  });

  it("copes with a chain without rows and a row without a leg", () => {
    expect(chainRowsOf({} as LegChainDto)).toEqual([]);
    expect(chainRowsOf({ rows: [{}] } as unknown as LegChainDto)[0]).toMatchObject({ key: "row-0", id: null, readOnly: false });
  });

  it("splitLocal is empty for nothing and for an unreadable value", () => {
    expect(splitLocal(null)).toEqual({ date: null, time: "" });
    expect(splitLocal("nope")).toEqual({ date: null, time: "" });
  });
});

describe("chainUpdateOf", () => {
  it("sends every row with its id (null for a new one), the trimmed values and the window instants", () => {
    const rows = [
      row("l1", "A", "B", { remarks: "  note ", origin: end("A", { reference: " REF ", windowFromDate: "2026-11-10", windowFromTime: "08:00", windowTillDate: "2026-11-10", windowTillTime: "" }) }),
      row("new-1", "B", "C", { id: null, remarks: "  " }),
    ];
    const update = chainUpdateOf(rows, dto());
    expect(update.rows![0]).toEqual({
      id: "l1",
      remarks: "note",
      origin: { waypointTypeId: "load", relationId: "A", relationAddressId: "addr-A", plannedDate: "2026-11-10", windowFrom: new Date(2026, 10, 10, 8, 0).toISOString(), windowTill: new Date(2026, 10, 10, 0, 0).toISOString(), reference: "REF" },
      destination: { waypointTypeId: "load", relationId: "B", relationAddressId: "addr-B", plannedDate: "2026-11-10", windowFrom: null, windowTill: null, reference: null },
    });
    expect(update.rows![1]).toMatchObject({ id: null, remarks: null });
  });

  it("a time without a date is no window", () => {
    const update = chainUpdateOf([row("l1", "A", "B", { origin: end("A", { windowFromTime: "08:00" }) })], dto());
    expect(update.rows![0].origin!.windowFrom).toBeNull();
  });

  it("carries the stamp of every leg as read, also of one the planner removed: the server must see it", () => {
    const update = chainUpdateOf([row("l1", "A", "B")], dto());
    expect(update.stamps).toEqual([{ id: "l1", concurrencyStamp: "s1" }, { id: "l2", concurrencyStamp: "s2" }]);
  });

  it("leaves a new row out of the stamps and copes with a missing stamp", () => {
    const chainDto = { rows: [{ leg: { id: "l1" } }, { leg: {} }] } as unknown as LegChainDto;
    expect(chainUpdateOf([], chainDto).stamps).toEqual([{ id: "l1", concurrencyStamp: "" }]);
  });

  it("sends an empty relation as undefined so the server's required check speaks", () => {
    const update = chainUpdateOf([row("l1", null, "B", { origin: end(null, { waypointTypeId: null, plannedDate: null }) })], dto());
    expect(update.rows![0].origin).toMatchObject({ relationId: undefined, waypointTypeId: undefined, plannedDate: undefined });
  });
});
