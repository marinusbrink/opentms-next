import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { Combobox as ComboboxPrimitive } from "@base-ui/react";
import { keepPreviousData, useInfiniteQuery, useQueryClient, type InfiniteData } from "@tanstack/react-query";
import { Check, Pencil, X } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Combobox, ComboboxCollection, ComboboxContent, ComboboxCreateItem, ComboboxEmpty, ComboboxInput, ComboboxList, ComboboxTrigger } from "@/components/ui/combobox";
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

/** The query behind one term of an EntityLookup: shared by the list and by a keystroke that waits for its results. */
function entityLookupPagesOptions<T>(key: string, term: string, fetchPage: (q: EntityLookupQuery) => Promise<EntityLookupPage<T>>) {
  return {
    queryKey: ["entity-lookup", key, term],
    queryFn: ({ pageParam }: { pageParam: number }) => fetchPage({ term, skip: pageParam, take: ENTITY_LOOKUP_PAGE_SIZE }),
    initialPageParam: 0,
    getNextPageParam: (last: EntityLookupPage<T>, pages: EntityLookupPage<T>[]) => {
      const loaded = pages.reduce((n, page) => n + page.items.length, 0);
      return last.items.length > 0 && loaded < last.totalCount ? loaded : undefined;
    },
  };
}

/** The paged, server-searched list behind an EntityLookup; `enabled` gates the request (popup open, term long enough). */
export function useEntityLookupPages<T>(key: string, term: string, fetchPage: (q: EntityLookupQuery) => Promise<EntityLookupPage<T>>, enabled: boolean) {
  return useInfiniteQuery({ ...entityLookupPagesOptions(key, term, fetchPage), enabled, placeholderData: keepPreviousData });
}

/** The id of the "Add new …" row, the list's last option (UI-LOOK-004; ArrowDown reaches it). */
const CREATE_ROW_ID = "\u0000entity-lookup-create";

/** The row a typed term picks when the user did not choose one: its exact code or name, else the first. */
function bestMatch<T>(items: readonly T[], term: string, getLabel: (item: T) => string): T | undefined {
  const typed = term.trim().toLowerCase();
  const exact = items.find((item) => {
    const code = (item as { code?: unknown }).code;
    return getLabel(item).trim().toLowerCase() === typed || (typeof code === "string" && code.trim().toLowerCase() === typed);
  });
  return exact ?? items[0];
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
  const queryKey = `${id}:${instanceKey}`;
  const query = useEntityLookupPages<T>(queryKey, searchTerm, fetchPage, canFetch);
  const results: T[] = belowMinimum ? [] : (query.data?.pages.flatMap((page) => page.items) ?? []);
  const loading = canFetch && query.isFetching && !query.isFetchingNextPage;
  const { hasNextPage, isFetchingNextPage, fetchNextPage } = query;
  /* The list answers the typed term: no debounce, request or previous term's rows in between. */
  const settled = !belowMinimum && searchTerm === trimmed && query.data !== undefined && !query.isPlaceholderData && !loading;
  const queryClient = useQueryClient();
  /* The "Add new …" row is the list's last option; with nothing found it is the only one. */
  const createRow = useRef({ id: CREATE_ROW_ID } as T).current;
  const isCreateRow = (item: T | null | undefined) => item != null && item.id === CREATE_ROW_ID;
  const items: T[] = onCreateNew ? [...results, createRow] : results;

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

  /* The highlighted option and whether the user moved there (keyboard, pointer) or it was highlighted for them. */
  const highlightedRef = useRef<{ item: T | undefined; chosen: boolean }>({ item: undefined, chosen: false });
  const openCreate = (typed: string) => {
    if (!onCreateNew) return;
    /* The editor opens on top: the list closes first. */
    setOpen(false);
    setTerm("");
    onCreateNew(typed);
  };
  const commit = (item: T) => {
    if (props.multiple) return;
    if (!(props.value && props.value.id === item.id)) props.onChange(item);
  };

  /* A keystroke that came before the results of its term (design 61 "Order page, revised" 3, keyboard first): it
   * waits for them through the same query the list uses. A later keystroke in this control supersedes it. */
  const keystroke = useRef(0);
  const settleLater = (typed: string, key: "Enter" | "Tab") => {
    const seq = ++keystroke.current;
    void queryClient
      .fetchInfiniteQuery(entityLookupPagesOptions(queryKey, typed, fetchPage))
      .then((data: InfiniteData<EntityLookupPage<T>, number>) => {
        if (keystroke.current !== seq) return;
        const found = data.pages.flatMap((page) => page.items);
        const match = bestMatch(found, typed, getLabel);
        if (match) {
          if (key === "Enter") {
            setOpen(false);
            setTerm("");
          }
          commit(match);
        } else if (key === "Enter") {
          openCreate(typed);
        }
      })
      .catch(() => undefined);
  };

  const onInputKeyDown = (event: React.KeyboardEvent<HTMLInputElement> & { preventBaseUIHandler: () => void }) => {
    if (event.key !== "Tab") keystroke.current++;
    if (props.multiple) return;
    /* UI-KEY-002: Escape closes the list and never clears the value (Base UI clears it on a closed list). */
    if (event.key === "Escape") {
      if (!open) event.preventBaseUIHandler();
      return;
    }
    if (event.key !== "Enter" && event.key !== "Tab") return;
    if (!open) return;
    const { item: highlighted, chosen } = highlightedRef.current;
    if (trimmed.length === 0) {
      /* Nothing typed: Enter is Base UI's (it selects an option the user moved to); Tab commits only such an option. */
      if (event.key === "Tab" && chosen && highlighted && !isCreateRow(highlighted)) commit(highlighted);
      return;
    }
    if (event.key === "Enter") {
      /* The user's own choice (also "Add new …" reached with ArrowDown) is Base UI's to select. */
      if (chosen && highlighted && (settled || isCreateRow(highlighted))) return;
      event.preventBaseUIHandler();
      event.preventDefault();
      event.stopPropagation();
      if (belowMinimum) return;
      if (!settled) {
        settleLater(trimmed, "Enter");
        return;
      }
      /* Settled: the highlighted match (the first, highlighted for the user), or "Add new …" for an empty result. */
      const match = highlighted && !isCreateRow(highlighted) ? highlighted : results[0];
      if (match) {
        setOpen(false);
        setTerm("");
        commit(match);
      } else {
        openCreate(trimmed);
      }
      return;
    }
    /* UI-KEY-004: Tab commits the highlighted match and focus moves on (Base UI closes on Tab without selecting). */
    if (belowMinimum) return;
    if (!settled && !(chosen && highlighted && !isCreateRow(highlighted))) {
      settleLater(trimmed, "Tab");
      return;
    }
    const match = highlighted && !isCreateRow(highlighted) ? highlighted : undefined;
    if (match) commit(match);
  };

  const selectedList: T[] = props.multiple ? props.value : props.value ? [props.value] : [];
  const isSelected = (item: T) => selectedList.some((v) => v.id === item.id);
  const visible = selectedList.slice(0, 3).map(getLabel);
  const rest = selectedList.length - visible.length;
  const summary = rest > 0 ? `${visible.join(", ")}, ${t("MoreSelected").replace("{0}", String(rest))}` : visible.join(", ");
  const canClear = !required && !disabled;

  const createText = createLabel ?? t("AddNew");
  const renderItem = (item: T) =>
    isCreateRow(item) ? (
      <ComboboxCreateItem key={CREATE_ROW_ID} value={item} label={createText} onCreate={() => openCreate(trimmed)} />
    ) : (
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

  const status = belowMinimum ? t("TypeToSearch") : loading ? <Spinner aria-label={t("Loading")} /> : t("NoItemsFound");
  const popup = (
    <ComboboxContent>
      {props.multiple && <ComboboxInput placeholder={t("Search")} showTrigger={false} aria-label={t("Search")} />}
      <ComboboxEmpty>{status}</ComboboxEmpty>
      <ComboboxList ref={listRef}>
        {/* With "Add new …" the list is never empty: the status of an empty result stands above that row. */}
        {onCreateNew && results.length === 0 && <div data-slot="entity-lookup-status" className="flex w-full justify-center py-2 text-center text-sm text-muted-foreground">{status}</div>}
        <ComboboxCollection>{renderItem}</ComboboxCollection>
        {results.length > 0 && loading && (
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
    </ComboboxContent>
  );

  const shared = {
    items,
    filter: null,
    disabled,
    open,
    isItemEqualToValue: (a: T, b: T) => a.id === b.id,
    itemToStringLabel: (item: T) => (isCreateRow(item) ? "" : getLabel(item)),
    /* The first match is highlighted once the results of a typed term arrive, also when they arrive after the
     * keystrokes ("always" re-highlights an empty highlight; Base UI types only the boolean). */
    autoHighlight: (trimmed.length > 0 ? "always" : true) as boolean,
    onOpenChange: (next: boolean) => {
      setOpen(next);
      if (!next) setTerm("");
    },
    onInputValueChange: (next: string, details: { reason: string }) => {
      if (details.reason === "input-change" || details.reason === "input-paste" || details.reason === "input-clear") setTerm(next);
    },
    onItemHighlighted: (item: T | undefined, details: { reason: string }) => {
      highlightedRef.current = { item, chosen: item !== undefined && details.reason !== "none" };
    },
  };

  if (props.multiple) {
    const { value, onChange } = props;
    return (
      <Combobox multiple {...shared} value={value} onValueChange={(next: T[]) => onChange(next.filter((item) => !isCreateRow(item)))}>
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
    <Combobox {...shared} value={value} onValueChange={(next: T | null) => (isCreateRow(next) ? undefined : onChange(next))}>
      <ComboboxInput
        id={id}
        placeholder={placeholder}
        disabled={disabled}
        showClear={canClear}
        className={className}
        onKeyDown={onInputKeyDown}
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
