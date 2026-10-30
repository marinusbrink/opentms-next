import { useEffect, useMemo, useState } from "react";

import { pricePreviewRequestOf, type EditorMode, type OrderValues } from "@/apps/transport/orders/order-form";
import { previewNewOrderPrices, previewPrices, type PricePreviewDto } from "@/domains/orders/transport-orders";

/* The price preview of an unsaved change (pricing §4.3; design 61 "Order page, legacy tab structure" 4 and 6: it applies per freight item
 * and to the totals): debounced, pure, never stored, and replaced by the stored lines after the save. Only a
 * priceable input is previewed (finding 1): every item has a transport unit and two stops with a relation and an
 * address. Below that, or when the server cannot price it, the page shows a quiet note: never a toast. A new order,
 * never saved, is previewed as it stands once its customer is chosen (Marinus 2026-10-30): there is no stored state to
 * compare with, so it needs no change to be previewed. */

export const PREVIEW_DEBOUNCE_MS = 600;

export interface PricePreviewState {
  preview: PricePreviewDto | null;
  previewing: boolean;
  /** The server refused or failed the preview. */
  failed: boolean;
  /** The unsaved change cannot be priced yet. */
  incomplete: boolean;
}

export function usePricePreview(mode: EditorMode, orderId: string | null | undefined, dirty: boolean, values: Pick<OrderValues, "customer" | "orderDate" | "terms" | "lines">): PricePreviewState {
  const [preview, setPreview] = useState<PricePreviewDto | null>(null);
  const [previewing, setPreviewing] = useState(false);
  const [failed, setFailed] = useState(false);
  const { customer, orderDate, terms, lines } = values;
  const input = useMemo(
    () => (mode !== "order" || (orderId ? !dirty : !customer?.id) ? undefined : pricePreviewRequestOf({ customer, orderDate, terms, lines })),
    [mode, orderId, dirty, customer, orderDate, terms, lines],
  );
  const request = input ? JSON.stringify(input) : null;
  useEffect(() => {
    setFailed(false);
    if (!request) {
      setPreview(null);
      return;
    }
    let cancelled = false;
    const handle = setTimeout(() => {
      setPreviewing(true);
      const body = JSON.parse(request);
      (orderId ? previewPrices(orderId, body) : previewNewOrderPrices(body))
        .then((result) => {
          if (!cancelled) setPreview(result);
        })
        .catch(() => {
          if (!cancelled) {
            setPreview(null);
            setFailed(true);
          }
        })
        .finally(() => {
          if (!cancelled) setPreviewing(false);
        });
    }, PREVIEW_DEBOUNCE_MS);
    return () => {
      cancelled = true;
      clearTimeout(handle);
    };
  }, [request, orderId]);
  return { preview, previewing, failed, incomplete: input === null };
}
