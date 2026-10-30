import { useEffect, useMemo, useRef, useState } from "react";
import { Plus, X } from "lucide-react";

import { Button } from "@/components/ui/button";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { MultiSelect, type MultiSelectOption } from "@/components/ui/multi-select";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { REVENUE_BASES, orderedRevenueBases, usePriceListLookupsByBasis, type KnownPriceList, type PriceListsPerBasisValue, type QuantitySource } from "@/domains/financial/price-lists";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The revenue price lists per basis (design 61 "Order page review" 3, the legacy block "Price calculation", QP5 kept;
 * compacted on the PO's request of 2026-10-28): one compact row per basis that is charged, in the legacy order, the
 * basis name plus a MultiSelect of the revenue lists of that basis (normally one; the model allows more) and a remove
 * button; "Add price basis" offers the bases not charged yet. A basis counts as charged when it is switched on or one of
 * its lists is chosen (build note 1). Removing a row clears its lists; a row without a list makes the calculation write
 * an open line ("To be priced") per freight item. The value is what the terms store: the chosen lists in their order and
 * the bases switched on. Used by the order's Financial tab (order and template) and the relation financial settings
 * editor; a list created in place joins the row of its basis (`withCreatedList`).
 *
 * Only the shown rows load their lookup; a chosen list whose basis the record does not know loads them all, to find it. */

export interface PriceListsPerBasisProps {
  /** Prefix of the controls' ids: `<prefix>-<basis>-lists`. */
  idPrefix: string;
  value: PriceListsPerBasisValue;
  onChange: (next: PriceListsPerBasisValue) => void;
  known?: KnownPriceList[];
  /** Read-only, or a business state that blocks the rows (the order's fixed price). */
  disabled?: boolean;
  /** "Add new …" in every row's picker (UI-LOOK-004): the parent's create-in-place offer, which adds the new list to the
   *  latest value through `withCreatedList`. */
  createInPlace?: {
    onCreateNew?: (term: string) => void;
    createLabel?: string;
  };
  /** Load the lookups (a dialog that is not open yet passes false). */
  enabled?: boolean;
}

export function PriceListsPerBasis({ idPrefix, value, onChange, known = [], disabled = false, createInPlace, enabled = true }: PriceListsPerBasisProps) {
  const { t } = useL();
  const knownBasis = useMemo(() => new Map(known.map((k) => [k.id, k.basis])), [known]);
  const unknownChosen = value.priceListIds.some((id) => !knownBasis.has(id));
  const charged = orderedRevenueBases([...value.chargedBases, ...value.priceListIds.flatMap((id) => knownBasis.get(id) ?? [])]);
  const lookups = usePriceListLookupsByBasis(1, (basis) => enabled && (unknownChosen || charged.includes(basis)));
  /* useQueries hands back a new array every render; the lookups change when one of them was (re)fetched. */
  const lookupsKey = lookups.map((q) => q.dataUpdatedAt).join(",");

  /* The options per basis (the lookup of that basis, plus the record's known lists of that basis it does not return)
   * and the basis of every list: the record's own knowledge first, else the first basis whose lookup returns it. */
  const { optionsByBasis, basisOf } = useMemo(() => {
    const byId = new Map(knownBasis);
    const options = new Map<QuantitySource, MultiSelectOption[]>();
    REVENUE_BASES.forEach((basis, index) => {
      const list = (lookups[index]?.data ?? [])
        .map((l) => ({
          id: l.id ?? "",
          label: [l.code, l.name].filter(Boolean).join(" "),
        }))
        .filter((o) => o.id);
      for (const o of list) if (!byId.has(o.id)) byId.set(o.id, basis.value);
      options.set(basis.value, list);
    });
    for (const k of known) {
      const list = options.get(k.basis);
      if (list && !list.some((o) => o.id === k.id)) list.push({ id: k.id, label: k.label });
    }
    return { optionsByBasis: options, basisOf: (id: string) => byId.get(id) };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the lookups through lookupsKey
  }, [known, knownBasis, lookupsKey]);

  const idsOf = (basis: QuantitySource) => value.priceListIds.filter((id) => basisOf(id) === basis);
  const shown = orderedRevenueBases([...charged, ...value.priceListIds.flatMap((id) => basisOf(id) ?? [])]);
  const addable = REVENUE_BASES.filter((b) => !shown.includes(b.value));

  /* A basis added from the menu gets the focus in its row's picker, keyboard first: the menu leaves the focus alone on
   * that close (instead of handing it back to its trigger) and the row takes it once rendered. */
  const [focusBasis, setFocusBasis] = useState<QuantitySource | null>(null);
  useEffect(() => {
    if (focusBasis == null) return;
    document.getElementById(`${idPrefix}-${focusBasis}-lists`)?.focus();
    setFocusBasis(null);
  }, [focusBasis, idPrefix]);
  const addedRef = useRef(false);
  const menuFinalFocus = () => {
    const added = addedRef.current;
    addedRef.current = false;
    return !added;
  };

  /* Adding without a list leaves an open line per freight item; removing clears the row's lists. */
  const add = (basis: QuantitySource) => {
    onChange({
      priceListIds: value.priceListIds,
      chargedBases: orderedRevenueBases([...value.chargedBases, basis]),
    });
    addedRef.current = true;
    setFocusBasis(basis);
  };
  const remove = (basis: QuantitySource) =>
    onChange({
      priceListIds: value.priceListIds.filter((id) => basisOf(id) !== basis),
      chargedBases: value.chargedBases.filter((b) => b !== basis),
    });
  /* A choice in a row keeps the order of the lists already chosen and appends the new ones; the row stays charged. */
  const choose = (basis: QuantitySource, next: string[]) =>
    onChange({
      priceListIds: [...value.priceListIds.filter((id) => basisOf(id) !== basis || next.includes(id)), ...next.filter((id) => !value.priceListIds.includes(id))],
      chargedBases: orderedRevenueBases([...value.chargedBases, basis]),
    });

  return (
    <div className="flex flex-col gap-2" data-slot="price-lists-per-basis">
      {shown.length === 0 && (
        <p className="text-label text-muted-foreground" data-slot="no-charged-bases">
          {t("Financial:NoChargedBases")}
        </p>
      )}
      {shown.length > 0 && (
        <div className="grid grid-cols-[auto_1fr_auto] items-center gap-x-3 gap-y-2">
          {shown.map((b) => {
            const basis = REVENUE_BASES.find((x) => x.value === b)!;
            const label = t(basis.key);
            const listsId = `${idPrefix}-${b}-lists`;
            return (
              <div key={b} className="contents" data-slot="charged-basis-row" data-basis={b}>
                <label className="text-sm" htmlFor={listsId} title={label}>
                  {label}
                </label>
                <MultiSelect
                  id={listsId}
                  value={idsOf(b)}
                  onChange={(next) => choose(b, next)}
                  options={optionsByBasis.get(b) ?? []}
                  disabled={disabled}
                  onCreateNew={disabled ? undefined : createInPlace?.onCreateNew}
                  createLabel={createInPlace?.createLabel}
                />
                <Tooltip>
                  <TooltipTrigger
                    render={<Button type="button" size="icon-sm" variant="ghost" disabled={disabled} aria-label={`${t("Financial:RemoveChargedBasis")}: ${label}`} onClick={() => remove(b)} />}
                  >
                    <X aria-hidden="true" />
                  </TooltipTrigger>
                  <TooltipContent>{t("Financial:RemoveChargedBasis")}</TooltipContent>
                </Tooltip>
              </div>
            );
          })}
        </div>
      )}
      {addable.length > 0 && (
        <DropdownMenu>
          <DropdownMenuTrigger
            disabled={disabled}
            render={
              <Button type="button" size="sm" variant="outline" className="self-start" data-slot="add-charged-basis">
                <Plus aria-hidden="true" /> {t("Financial:AddChargedBasis")}
              </Button>
            }
          />
          <DropdownMenuContent align="start" className="w-auto" finalFocus={menuFinalFocus}>
            {addable.map((b) => (
              <DropdownMenuItem key={b.value} onClick={() => add(b.value)}>
                {t(b.key)}
              </DropdownMenuItem>
            ))}
          </DropdownMenuContent>
        </DropdownMenu>
      )}
    </div>
  );
}
