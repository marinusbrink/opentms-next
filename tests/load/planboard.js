// k6 load script for the Planboard reads and commands (design 66, D1: p95 < 200 ms server time on a warm instance at
// 1,000 open trips, 5,000 unplanned legs, 300 vehicles and 300 drivers per tenant).
//
// Run against a tenant loaded with the volume dataset (Tenants app → test datasets):
//   k6 run -e BASE_URL=http://localhost:44301 -e TOKEN=<bearer token of a tenant planner> tests/load/planboard.js
// Optional: -e FROM=2026-11-02 (first day of the window; default today), -e TRIP_ID=<a trip for the map read>.
// The nightly load run (performance budgets) reports the p95 per endpoint; a threshold breach fails the run.

import http from "k6/http";
import { check, group } from "k6";

const BASE = __ENV.BASE_URL || "http://localhost:44301";
const TOKEN = __ENV.TOKEN;
const FROM = __ENV.FROM || new Date().toISOString().slice(0, 10);

function addDays(date, days) {
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

const TO = addDays(FROM, 6);
const params = { headers: { Authorization: `Bearer ${TOKEN}`, Accept: "application/json" } };

const endpoints = ["scheduler", "trips", "legs", "legGroups", "orders", "mapTrips", "tripMap", "drivers", "availability"];

export const options = {
  scenarios: { planners: { executor: "constant-vus", vus: 10, duration: "2m" } },
  thresholds: Object.fromEntries(endpoints.map((ep) => [`http_req_duration{ep:${ep}}`, ["p(95)<200"]])),
};

function get(ep, path) {
  const res = http.get(`${BASE}${path}`, { ...params, tags: { ep } });
  check(res, { [`${ep} 200`]: (r) => r.status === 200 });
  return res;
}

export default function () {
  group("scheduler", () => {
    for (const mode of ["Tractor", "Trailer", "Driver"]) {
      get("scheduler", `/api/planning/planboard/scheduler?Mode=${mode}&From=${FROM}T00:00:00Z&To=${TO}T23:59:59Z&Take=50`);
    }
  });

  group("lists", () => {
    const trips = get("trips", `/api/planning/transport-trip?StartRow=0&EndRow=100&ExecutionFrom=${FROM}T00:00:00Z&ExecutionTo=${TO}T23:59:59Z`);
    get("legs", `/api/planning/leg?StartRow=0&EndRow=100&DateFrom=${FROM}&DateTo=${TO}`);
    get("legGroups", `/api/planning/leg/groups?GroupBy=customer&DateFrom=${FROM}&DateTo=${TO}&StartRow=0&EndRow=100`);
    get("orders", `/api/orders/planboard-order?StartRow=0&EndRow=100&PlannedFrom=${FROM}&PlannedTo=${TO}`);

    const tripId = __ENV.TRIP_ID || (trips.status === 200 ? (trips.json("rows") || [])[0]?.id : undefined);
    if (tripId) {
      get("tripMap", `/api/planning/planboard/${tripId}/map`);
    }
  });

  group("map and resources", () => {
    get("mapTrips", `/api/planning/planboard/map-trips?From=${FROM}T00:00:00Z&To=${TO}T23:59:59Z`);
    const drivers = get("drivers", `/api/planning/planboard/resource-drivers?From=${FROM}T00:00:00Z&To=${TO}T23:59:59Z&Take=50`);
    const ids = drivers.status === 200 ? (drivers.json("items") || []).slice(0, 50).map((d) => `ResourceIds=${d.id}`) : [];
    if (ids.length > 0) {
      get("availability", `/api/planning/resource-availability?ResourceKind=2&From=${FROM}T00:00:00Z&To=${TO}T23:59:59Z&${ids.join("&")}`);
    }
  });
}
