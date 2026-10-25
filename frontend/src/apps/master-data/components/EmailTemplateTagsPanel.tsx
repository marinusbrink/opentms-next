import { useEffect, useMemo, useState } from "react";
import { ChevronDown, ChevronRight, Search } from "lucide-react";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { FORM_SECTION_CAPTION } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";
import {
  EMAIL_TEMPLATE_TAG_KIND_COLLECTION,
  useEmailTemplateTags,
  type EmailTemplatePurpose,
  type EmailTemplateTag,
  type EmailTemplateTagGroup,
} from "@/domains/platform/master-data";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* The Tags panel of the e-mail template editor (design 54, "Tags and default content", PO 2026-10-20): the localized
 * tag groups of the chosen purpose, each tag a button that inserts its snippet where the cursor is (`{{ company.email }}`
 * for a value, the `{{ for … }} … {{ end }}` block for a collection, `{{ line.product }}` for a collection's child,
 * shown indented). The editor decides where the text goes (the field last focused, by default the body); the panel
 * only reports the click. A filter box narrows the rows; the panel and each group collapse. The collapsed state of the
 * panel is remembered per browser (localStorage, best effort). The panel is not rendered in read-only mode. */

export interface EmailTemplateTagsPanelProps {
  purpose: EmailTemplatePurpose;
  /** Inserts `text` where the cursor of the target field is. */
  onInsert: (text: string) => void;
  className?: string;
}

const COLLAPSED_STORAGE_KEY = "opentms.emailTemplate.tagsCollapsed";

const readCollapsed = (): boolean => {
  try {
    return window.localStorage.getItem(COLLAPSED_STORAGE_KEY) === "1";
  } catch {
    return false;
  }
};

const writeCollapsed = (collapsed: boolean): void => {
  try {
    window.localStorage.setItem(COLLAPSED_STORAGE_KEY, collapsed ? "1" : "0");
  } catch {
    /* Private mode or a full store: the state is a convenience, not data. */
  }
};

const matches = (tag: EmailTemplateTag, needle: string): boolean =>
  [tag.label, tag.path, tag.description].some((text) => (text ?? "").toLowerCase().includes(needle));

/** The group's tags that match the filter; a collection whose child matches stays with the matching children only. */
function filterTags(tags: EmailTemplateTag[], needle: string): EmailTemplateTag[] {
  if (!needle) return tags;
  const kept: EmailTemplateTag[] = [];
  for (const tag of tags) {
    const children = (tag.children ?? []).filter((child) => matches(child, needle));
    if (matches(tag, needle)) kept.push(tag);
    else if (children.length > 0) kept.push({ ...tag, children });
  }
  return kept;
}

function TagRow({ tag, indent, onInsert, insertLabel }: { tag: EmailTemplateTag; indent: boolean; onInsert: (text: string) => void; insertLabel: string }) {
  const description = tag.description?.trim();
  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <button
            type="button"
            /* Keep the focus (and the caret) in the field the tag goes into: the click only reports the text. */
            onMouseDown={(e) => e.preventDefault()}
            onClick={() => onInsert(tag.snippet ?? "")}
            className={cn(
              "flex w-full flex-col items-start gap-0.5 rounded-md px-2 py-1 text-left transition-colors outline-none hover:bg-accent hover:text-accent-foreground focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50",
              indent && "pl-6",
            )}
          />
        }
      >
        <span className="text-sm">{tag.label}</span>
        {/* font-mono: a Scriban path, raw technical content (UI-TOK-008). */}
        <code className="font-mono text-label text-muted-foreground">{tag.path}</code>
      </TooltipTrigger>
      <TooltipContent side="left">{description || insertLabel}</TooltipContent>
    </Tooltip>
  );
}

function TagGroup({
  group,
  forceOpen,
  onInsert,
  insertLabel,
}: {
  group: EmailTemplateTagGroup;
  /** A filter is active: every group shows its matches. */
  forceOpen: boolean;
  onInsert: (text: string) => void;
  insertLabel: string;
}) {
  const { t } = useL();
  const [open, setOpen] = useState(true);
  const expanded = forceOpen || open;
  const tags = group.tags ?? [];
  const panelId = `email-template-tags-${group.group ?? ""}`;
  return (
    <li>
      <button
        type="button"
        aria-expanded={expanded}
        aria-controls={panelId}
        aria-label={`${expanded ? t("Collapse") : t("Expand")} ${group.label ?? ""}`}
        onMouseDown={(e) => e.preventDefault()}
        onClick={() => setOpen((o) => !o)}
        className="flex w-full items-center gap-1 rounded-md px-1 py-1 text-label font-semibold text-brand outline-none hover:bg-accent focus-visible:ring-3 focus-visible:ring-ring/50"
      >
        {expanded ? <ChevronDown aria-hidden className="size-4 shrink-0" /> : <ChevronRight aria-hidden className="size-4 shrink-0" />}
        <span className="min-w-0 flex-1 truncate text-left">{group.label}</span>
        <span className="text-muted-foreground tabular-nums">{tags.reduce((n, tag) => n + 1 + (tag.children?.length ?? 0), 0)}</span>
      </button>
      {expanded && (
        <ul id={panelId} className="flex flex-col">
          {tags.map((tag) => (
            <li key={tag.path}>
              <TagRow tag={tag} indent={false} onInsert={onInsert} insertLabel={insertLabel} />
              {tag.kind === EMAIL_TEMPLATE_TAG_KIND_COLLECTION && (tag.children?.length ?? 0) > 0 && (
                <ul className="flex flex-col">
                  {tag.children!.map((child) => (
                    <li key={child.path}>
                      <TagRow tag={child} indent onInsert={onInsert} insertLabel={insertLabel} />
                    </li>
                  ))}
                </ul>
              )}
            </li>
          ))}
        </ul>
      )}
    </li>
  );
}

export function EmailTemplateTagsPanel({ purpose, onInsert, className }: EmailTemplateTagsPanelProps) {
  const { t } = useL();
  const [collapsed, setCollapsed] = useState(readCollapsed);
  const [filter, setFilter] = useState("");
  const tags = useEmailTemplateTags(purpose, !collapsed);

  useEffect(() => {
    writeCollapsed(collapsed);
  }, [collapsed]);

  const needle = filter.trim().toLowerCase();
  const groups = useMemo(
    () => (tags.data ?? []).map((group) => ({ ...group, tags: filterTags(group.tags ?? [], needle) })).filter((group) => group.tags.length > 0),
    [tags.data, needle],
  );
  const insertLabel = t("Platform:InsertTag");
  const headingId = "email-template-tags-heading";
  const listId = "email-template-tags-list";

  return (
    <section
      aria-labelledby={headingId}
      data-slot="email-template-tags"
      /* UI-DLG-006: the caption, hint and filter stay fixed, only the groups list scrolls (when the editor gives the
       * panel a height). Collapsed, the panel hugs its caption instead of standing as a tall empty card. */
      className={cn(
        "flex flex-col gap-3 rounded-lg border border-border-section bg-surface-section p-4",
        className,
        collapsed && "gap-0 lg:h-auto lg:self-start",
      )}
    >
      <div className="flex items-center gap-2">
        <h3 id={headingId} className={cn("min-w-0 flex-1", FORM_SECTION_CAPTION)}>
          {t("Platform:Tags")}
        </h3>
        <Button
          type="button"
          variant="ghost"
          size="icon-sm"
          aria-expanded={!collapsed}
          aria-controls={listId}
          aria-label={collapsed ? t("Expand") : t("Collapse")}
          onClick={() => setCollapsed((c) => !c)}
        >
          {collapsed ? <ChevronRight aria-hidden /> : <ChevronDown aria-hidden />}
        </Button>
      </div>
      {!collapsed && (
        <div id={listId} className="flex min-h-0 flex-1 flex-col gap-3">
          <p className="text-label text-muted-foreground">{t("Platform:TagsHint")}</p>
          <div className="relative">
            <Search aria-hidden className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
            {/* A filter box, not a form field: labelled and placeholdered like the grid toolbar's search box. */}
            <Input
              type="search"
              value={filter}
              placeholder={t("Platform:FilterTags")}
              aria-label={t("Platform:FilterTags")}
              aria-controls={listId}
              onChange={(e) => setFilter(e.target.value)}
              className="h-10 bg-background pl-8"
            />
          </div>
          {tags.isLoading ? (
            <div className="flex flex-col gap-2" aria-busy="true">
              <Skeleton className="h-6 w-1/2" />
              <Skeleton className="h-8 w-full" />
              <Skeleton className="h-8 w-full" />
              <Skeleton className="h-8 w-3/4" />
            </div>
          ) : tags.error != null ? (
            <Alert variant="destructive">
              <AlertDescription className="flex flex-col items-start gap-2">
                <span>{getApiErrorMessage(tags.error, t)}</span>
                <Button type="button" variant="outline" size="sm" onClick={() => void tags.refetch()}>
                  {t("Platform:RetryTags")}
                </Button>
              </AlertDescription>
            </Alert>
          ) : groups.length === 0 ? (
            <p className="px-1 text-sm text-muted-foreground">{needle ? t("Platform:NoTagsMatch") : t("Platform:NoTags")}</p>
          ) : (
            <TooltipProvider>
              {/* -m-1 p-1: the rows' focus rings keep their room inside the scroll container. */}
              <ul className="-m-1 flex min-h-0 flex-1 flex-col gap-1 overflow-y-auto p-1">
                {groups.map((group) => (
                  <TagGroup key={group.group} group={group} forceOpen={needle.length > 0} onInsert={onInsert} insertLabel={insertLabel} />
                ))}
              </ul>
            </TooltipProvider>
          )}
        </div>
      )}
    </section>
  );
}
