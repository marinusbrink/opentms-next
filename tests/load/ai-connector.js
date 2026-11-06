// k6 load script for the AI connector (designs 74 and 75): the /mcp overhead, get_customer_revenue and query_metrics.
// Budget: p95 < 300 ms for an interactive read on a warm instance. Design 75's worst case is a year's orders by customer and
// month, at 1,000,000 orders and 250,000 trips in one tenant. Design 74: 1,000,000 orders with 100,000 for one customer.
//
// Run against a tenant loaded with that volume, whose backfill has finished, with a connector token of a user holding
// Integrations.AiConnector.Use, Orders.TransportOrders, PlanningExecution.TransportTrips and Financial.RateLines:
//   k6 run -e BASE_URL=http://localhost:44301 -e TOKEN=<connector access token> -e CUSTOMER_ID=<the large customer> tests/load/ai-connector.js
// Optional: -e YEAR=2025 (the year asked for; default last year).
// /mcp allows 60 requests per minute per connection (AiConnectorRateLimits): one virtual user per token, paced below that
// limit, so a 429 here is a failure of the scenario and not of the budget. To load the host harder, run one k6 process per
// connection. The nightly load run (performance budgets) reports the p95 per endpoint; a threshold breach fails the run.

import http from "k6/http";
import { check, sleep } from "k6";

const BASE = __ENV.BASE_URL || "http://localhost:44301";
const TOKEN = __ENV.TOKEN;
const CUSTOMER_ID = __ENV.CUSTOMER_ID;
const YEAR = __ENV.YEAR || String(new Date().getUTCFullYear() - 1);
const FROM = `${YEAR}-01-01`;
const TO = `${YEAR}-12-31`;

const endpoints = ["toolsList", "listMetrics", "customerRevenue", "ordersByCustomerAndMonth", "tripsByCarrierAndMonth", "fiveYearsByMonth"];

export const options = {
  scenarios: { connector: { executor: "constant-vus", vus: 1, duration: "5m" } },
  thresholds: {
    ...Object.fromEntries(endpoints.map((ep) => [`http_req_duration{ep:${ep}}`, ["p(95)<300"]])),
    checks: ["rate>0.99"],
  },
};

const params = (ep) => ({
  headers: {
    Authorization: `Bearer ${TOKEN}`,
    "Content-Type": "application/json",
    Accept: "application/json, text/event-stream",
    "MCP-Protocol-Version": "2025-06-18",
  },
  tags: { ep },
});

let id = 0;

function rpc(ep, method, rpcParams) {
  id += 1;
  const res = http.post(`${BASE}/mcp`, JSON.stringify({ jsonrpc: "2.0", id, method, params: rpcParams }), params(ep));
  check(res, { [`${ep} 200`]: (r) => r.status === 200 });
  return res;
}

/** The tool's own answer, from either answer form of Streamable HTTP. */
function toolJson(res) {
  const body = res.body || "";
  const data = body.trimStart().startsWith("{")
    ? body
    : body
        .split("\n")
        .filter((line) => line.startsWith("data:"))
        .map((line) => line.slice(5).trim())
        .join("");
  return JSON.parse(JSON.parse(data).result.content[0].text);
}

function query(ep, args) {
  const res = rpc(ep, "tools/call", { name: "query_metrics", arguments: args });
  if (res.status === 200) {
    const answer = toolJson(res);
    check(answer, { [`${ep} ok`]: (a) => a.status === "ok" });
  }
}

export default function () {
  rpc("toolsList", "tools/list", {});
  rpc("listMetrics", "tools/call", { name: "list_metrics", arguments: {} });

  if (CUSTOMER_ID) {
    rpc("customerRevenue", "tools/call", {
      name: "get_customer_revenue",
      arguments: { customerRelationId: CUSTOMER_ID, dateFrom: FROM, dateTo: TO },
    });
  }

  query("ordersByCustomerAndMonth", {
    metrics: ["order_revenue", "order_margin", "order_count"],
    groupBy: ["customer", "month"],
    dateFrom: FROM,
    dateTo: TO,
  });
  query("tripsByCarrierAndMonth", {
    metrics: ["trip_cost", "trip_revenue", "planned_distance_km", "on_time_stop_percentage"],
    groupBy: ["carrier", "month"],
    dateFrom: FROM,
    dateTo: TO,
  });
  query("fiveYearsByMonth", {
    metrics: ["order_count"],
    groupBy: ["month"],
    dateFrom: `${Number(YEAR) - 4}-01-01`,
    dateTo: TO,
  });

  /* Six requests a round, at most 60 a minute. */
  sleep(6.5);
}
