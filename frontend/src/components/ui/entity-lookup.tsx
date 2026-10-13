import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { Combobox as ComboboxPrimitive } from "@base-ui/react";
import { keepPreviousData, useInfiniteQuery } from "@tanstack/react-query";
import { Check, Pencil, Plus, X } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Combobox, ComboboxCollection, ComboboxContent, ComboboxEmpty, ComboboxInput, ComboboxList, ComboboxTrigger } from "@/components/ui/combobox";
import { InputGroupAddon, InputGroupButton } from "@/components/ui/input-group";
import { Spinner } from "@/components/ui/spinner";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* The entity picker of UI-LOOK-001 for master-data and transactional records (component map
 * GAP-01, option (a)): the shadcn Combobox over a TanStack `useInfiniteQuery` list, searched on
 * the server (UI-LOOK-002: 250 ms debounce, 2 characters, pages of 25, the next page when the
 * sentinel at the end of the list becomes visible). The component never filters client-side.
 * Closed, the control shows `getLabel` only (UI-LOOK-003); "Add new …" (UI-LOOK-004) and the edit
 * pencil (UI-LOOK-005) appear only with `onCreateNew` / `onEdit`; optional lookups clear with an X
 * (UI-LOOK-006); multiple shows a comma list with "+N" and a checkbox per option (UI-LOOK-007). */

export const ENTITY_LOOKUP_PAGE_SIZE = 25;
export const ENTITY_LOOKUP_DEBOUNCE_MS = 250;
export const ENTITY_LOOKUP_MIN_TERM_LENGTH = 2;

export interface EntityLookupQuery {
  term: string;
  skip: number;
  take: number;
}

export interface EntityLookupPage<T> {
  items: T[];
  totalCount: number;
}

interface EntityLookupBaseProps<T extends { id: string }> {
  id: string;
  fetchPage: (q: EntityLookupQuery) => Promise<EntityLookupPage<T>>;
  /** Shown when selected: the name. */
  getLabel: (item: T) => string;
  /** A richer row in the list: the name in font-medium plus at most two lines in text-label text-muted-foreground. */
  renderOption?: (item: T) => ReactNode;
  /** Shows "Add new …" with the typed term; omit when the user may not create. */
  onCreateNew?: (term: string) => void;
  /** Shows the edit pencil for the selected record; omit when the user may not edit. */
  onEdit?: (item: T) => void;
  /** The label of the "Add new …" row, already translated. */
  createLabel?: string;
  disabled?: boolean;
  /** Required lookups have no clear button (UI-LOOK-006). */
  required?: boolean;
  "aria-invalid"?: boolean;
  "aria-describedby"?: string;
  "aria-required"?: boolean;
  placeholder?: string;
  className?: string;
}

export type EntityLookupProps<T extends { id: string }> = EntityLookupBaseProps<T> &
  (
    | { multiple?: false; value: T | null; onChange: (value: T | null) => void }
    | { multiple: true; value: T[]; onChange: (value: T[]) => void }
  );

/** The paged, server-searched list behind an EntityLookup; `enabled` gates the request (popup open, term long enough). */
export function useEntityLookupPages<T>(key: string, term: string, fetchPage: (q: EntityLookupQuery) => Promise<EntityLookupPage<T>>, enabled: boolean) {
  return useInfiniteQuery({
    queryKey: ["entity-lookup", key, term],
    queryFn: ({ pageParam }) => fetchPage({ term, skip: pageParam, take: ENTITY_LOOKUP_PAGE_SIZE }),
    initialPageParam: 0,
    getNextPageParam: (last, pages) => {
      const loaded = pages.reduce((n, page) => n + page.items.length, 0);
      return last.items.length > 0 && loaded < last.totalCount ? loaded : undefined;
    },
    enabled,
    placeholderData: keepPreviousData,
  });
}

function useDebounced(value: string, delay: number): string {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const handle = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(handle);
  }, [value, delay]);
  return debounced;
}

const ITEM_CLASS =
  "relative flex w-full cursor-default items-center gap-2 rounded-md py-1 pr-8 pl-1.5 text-sm outline-hidden select-none data-highlighted:bg-accent data-highlighted:text-accent-foreground data-disabled:pointer-events-none data-disabled:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0";

export function EntityLookup<T extends { id: string }>(props: EntityLookupProps<T>) {
  const { id, fetchPage, getLabel, renderOption, onCreateNew, onEdit, createLabel, disabled = false, required = false, placeholder, className } = props;
  const ariaInvalid = props["aria-invalid"];
  const ariaDescribedBy = props["aria-describedby"];
  const ariaRequired = props["aria-required"];
  const { t } = useL();
  const instanceKey = useId();
  const [open, setOpen] = useState(false);
  const [term, setTerm] = useState("");
  const debounced = useDebounced(term, ENTITY_LOOKUP_DEBOUNCE_MS);
  const listRef = useRef<HTMLDivElement | null>(null);
  const sentinelRef = useRef<HTMLDivElement | null>(null);

  const trimmed = term.trim();
  const belowMinimum = trimmed.length > 0 && trimmed.length < ENTITY_LOOKUP_MIN_TERM_LENGTH;
  const searchTerm = debounced.trim();
  const canFetch = open && !(searchTerm.length > 0 && searchTerm.length < ENTITY_LOOKUP_MIN_TERM_LENGTH);
  const query = useEntityLookupPages<T>(`${id}:${instanceKey}`, searchTerm, fetchPage, canFetch);
  const items: T[] = belowMinimum ? [] : (query.data?.pages.flatMap((page) => page.items) ?? []);
  const loading = canFetch && query.isFetching && !query.isFetchingNextPage;
  const { hasNextPage, isFetchingNextPage, fetchNextPage } = query;

  /* UI-LOOK-002: the next page loads when the sentinel at the end of the list becomes visible. */
  useEffect(() => {
    const sentinel = sentinelRef.current;
    if (!open || !sentinel || !hasNextPage || typeof IntersectionObserver === "undefined") return;
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting) && !isFetchingNextPage) void fetchNextPage();
      },
      { root: listRef.current },
    );
    observer.observe(sentinel);
    return () => observer.disconnect();
  }, [open, hasNextPage, isFetchingNextPage, fetchNextPage, items.length]);

  /* UI-KEY-004: Tab commits. Base UI closes on Tab without selecting, so the highlighted item (or the
   * only match of a typed term) is handed over before the popup closes. */
  const highlightedRef = useRef<T | undefined>(undefined);
  const commitOnTab = (event: React.KeyboardEvent) => {
    if (event.key !== "Tab" || !open || props.multiple) return;
    const candidate = highlightedRef.current ?? (trimmed.length > 0 && items.length === 1 ? items[0] : undefined);
    if (candidate && !(props.value && props.value.id === candidate.id)) props.onChange(candidate);
  };

  const selectedList: T[] = props.multiple ? props.value : props.value ? [props.value] : [];
  const isSelected = (item: T) => selectedList.some((v) => v.id === item.id);
  const visible = selectedList.slice(0, 3).map(getLabel);
  const rest = selectedList.length - visible.length;
  const summary = rest > 0 ? `${visible.join(", ")}, ${t("MoreSelected").replace("{0}", String(rest))}` : visible.join(", ");
  const canClear = !required && !disabled;

  const renderItem = (item: T) => (
    <ComboboxPrimitive.Item key={item.id} value={item} className={ITEM_CLASS}>
      {props.multiple && (
        <span
          aria-hidden="true"
          className={cn("flex size-4 shrink-0 items-center justify-center rounded-sm border border-input", isSelected(item) && "border-primary bg-primary text-primary-foreground")}
        >
          {isSelected(item) && <Check className="size-3.5" />}
        </span>
      )}
      <div className="line-clamp-3 min-w-0 flex-1">{renderOption ? renderOption(item) : <span className="truncate">{getLabel(item)}</span>}</div>
      {!props.multiple && (
        <ComboboxPrimitive.ItemIndicator render={<span className="pointer-events-none absolute right-2 flex size-4 items-center justify-center" />}>
          <Check className="size-4" />
        </ComboboxPrimitive.ItemIndicator>
      )}
    </ComboboxPrimitive.Item>
  );

  const popup = (
    <ComboboxContent>
      {props.multiple && <ComboboxInput placeholder={t("Search")} showTrigger={false} aria-label={t("Search")} />}
      <ComboboxEmpty>
        {belowMinimum ? t("TypeToSearch") : loading ? <Spinner aria-label={t("Loading")} /> : t("NoItemsFound")}
      </ComboboxEmpty>
      <ComboboxList ref={listRef}>
        <ComboboxCollection>{renderItem}</ComboboxCollection>
        {items.length > 0 && loading && (
          <div className="flex justify-center py-2">
            <Spinner aria-label={t("Loading")} />
          </div>
        )}
        {hasNextPage && !belowMinimum && (
          <div ref={sentinelRef} data-slot="entity-lookup-sentinel" className="flex h-6 items-center justify-center">
            {isFetchingNextPage && <Spinner aria-label={t("Loading")} />}
          </div>
        )}
      </ComboboxList>
      {onCreateNew && (
        <div className="border-t border-border p-1">
          <Button type="button" variant="ghost" size="sm" className="w-full justify-start" onClick={() => onCreateNew(trimmed)}>
            <Plus aria-hidden="true" /> {createLabel ?? t("AddNew")}
          </Button>
        </div>
      )}
    </ComboboxContent>
  );

  const shared = {
    items,
    filter: null,
    disabled,
    open,
    isItemEqualToValue: (a: T, b: T) => a.id === b.id,
    itemToStringLabel: getLabel,
    onOpenChange: (next: boolean) => {
      setOpen(next);
      if (!next) setTerm("");
    },
    onInputValueChange: (next: string, details: { reason: string }) => {
      if (details.reason === "input-change" || details.reason === "input-paste" || details.reason === "input-clear") setTerm(next);
    },
    onItemHighlighted: (item: T | undefined) => {
      highlightedRef.current = item;
    },
  };

  if (props.multiple) {
    const { value, onChange } = props;
    return (
      <Combobox multiple {...shared} value={value} onValueChange={(next: T[]) => onChange(next)}>
        <div className={cn("relative", className)}>
          <ComboboxTrigger
            id={id}
            disabled={disabled}
            aria-invalid={ariaInvalid}
            aria-describedby={ariaDescribedBy}
            aria-required={ariaRequired}
            render={<Button type="button" variant="outline" className={cn("h-10 w-full justify-between bg-background px-3 font-normal", canClear && value.length > 0 && "pr-14")} />}
          >
            <span className="truncate text-left">{summary}</span>
          </ComboboxTrigger>
          {canClear && value.length > 0 && (
            <Button type="button" variant="ghost" size="icon-sm" aria-label={t("Clear")} onClick={() => onChange([])} className="absolute top-1/2 right-7 -translate-y-1/2">
              <X aria-hidden="true" />
            </Button>
          )}
        </div>
        {popup}
      </Combobox>
    );
  }

  const { value, onChange } = props;
  return (
    <Combobox {...shared} value={value} onValueChange={(next: T | null) => onChange(next)}>
      <ComboboxInput
        id={id}
        placeholder={placeholder}
        disabled={disabled}
        showClear={canClear}
        className={className}
        onKeyDown={commitOnTab}
        aria-invalid={ariaInvalid}
        aria-describedby={ariaDescribedBy}
        aria-required={ariaRequired}
      >
        {value && onEdit && (
          /* UI-LOOK-005: inside the control's right edge, before the clear button (the built-in addon is order-last). */
          <InputGroupAddon align="inline-end" className="order-1 pr-0">
            <Tooltip>
              <TooltipTrigger render={<InputGroupButton size="icon-sm" aria-label={t("Edit")} disabled={disabled} onClick={() => onEdit(value)} />}>
                <Pencil aria-hidden="true" />
              </TooltipTrigger>
              <TooltipContent>{t("Edit")}</TooltipContent>
            </Tooltip>
          </InputGroupAddon>
        )}
      </ComboboxInput>
      {popup}
    </Combobox>
  );
}
