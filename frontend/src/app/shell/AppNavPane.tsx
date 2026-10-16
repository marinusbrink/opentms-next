import { useState } from "react";
import { Link, useLocation } from "@tanstack/react-router";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { type AppDefinition, type AppView } from "@/app/apps.config";
import { useAppFeatures } from "@/app/app-features";
import { useIsGrantedChecker } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";
import { cn } from "@/lib/utils";

export interface AppNavPaneProps {
  app: AppDefinition;
  collapsed: boolean;
  onToggleCollapsed: () => void;
}

interface NavEntryProps {
  view: AppView;
  collapsed: boolean;
  onToggleCollapsed: () => void;
}

function matchesPath(path: string, pathname: string): boolean {
  return pathname === path || pathname.startsWith(`${path}/`);
}

/** Whether the current page is this node or lies anywhere below it. A list's path need not sit under its group's
 * path (Master data: /master-data/countries under the group /master-data/reference), so the tree decides. */
function containsPath(view: AppView, pathname: string): boolean {
  return matchesPath(view.path, pathname) || (view.children ?? []).some((child) => containsPath(child, pathname));
}

/* One node of the tree, at any depth (Master data has three levels: domain group, group, list; PO 2026-10-16). A
 * node's children are indented one step further than the node itself. */
function NavEntry({ view, collapsed, onToggleCollapsed }: NavEntryProps) {
  const { t } = useL();
  const location = useLocation();

  const isActive = containsPath(view, location.pathname);
  /* A category that holds the current page starts open, and opens when navigation moves into it, so the active
   * entry is always visible; the user may still close it. (State adjusted during render, not in an effect.) */
  const [childrenOpen, setChildrenOpen] = useState(isActive);
  const [wasActive, setWasActive] = useState(isActive);
  if (isActive !== wasActive) {
    setWasActive(isActive);
    if (isActive) setChildrenOpen(true);
  }
  const hasChildren = view.children != null && view.children.length > 0;
  const label = t(view.nameKey);
  const Icon = view.icon;

  function handleClick() {
    if (hasChildren) {
      if (collapsed) {
        /* The icon rail shows only the top level: expand the pane with this category open. */
        setChildrenOpen(true);
        onToggleCollapsed();
      } else {
        setChildrenOpen((v) => !v);
      }
    }
  }

  const iconRailClassName = cn(
    "flex w-full items-center justify-center rounded-md py-2 transition-colors",
    isActive
      ? "bg-brand text-brand-foreground"
      : "text-muted-foreground hover:bg-accent hover:text-accent-foreground",
  );

  const entryContent = collapsed ? (
    <TooltipProvider>
      <Tooltip>
        <TooltipTrigger
          render={
            hasChildren ? (
              <button
                onClick={handleClick}
                className={iconRailClassName}
                aria-label={label}
              />
            ) : (
              <Link
                to={view.path}
                aria-current={isActive ? "page" : undefined}
                aria-label={label}
                className={iconRailClassName}
              />
            )
          }
        >
          <Icon size={24} />
        </TooltipTrigger>
        <TooltipContent side="right">{label}</TooltipContent>
      </Tooltip>
    </TooltipProvider>
  ) : (
    <div className="relative">
      {hasChildren ? (
        <TooltipProvider>
          <Tooltip>
            <TooltipTrigger
              render={
                <button
                  onClick={handleClick}
                  className={cn(
                    "flex w-full items-center gap-3 rounded-md py-2 pl-3 pr-2 text-sm transition-colors hover:bg-accent hover:text-accent-foreground",
                    isActive ? "text-foreground font-medium" : "text-muted-foreground",
                  )}
                >
                  <Icon size={24} className="shrink-0" />
                  <span className="min-w-0 flex-1 truncate text-left">{label}</span>
                  <ChevronRight
                    size={16}
                    className={cn("shrink-0 transition-transform", childrenOpen && "rotate-90")}
                  />
                </button>
              }
            />
            <TooltipContent side="right">{label}</TooltipContent>
          </Tooltip>
        </TooltipProvider>
      ) : (
        <TooltipProvider>
          <Tooltip>
            <TooltipTrigger
              render={
                <Link
                  to={view.path}
                  aria-current={isActive ? "page" : undefined}
                  className={cn(
                    "flex w-full items-center gap-3 rounded-md py-2 pl-3 pr-2 text-sm transition-colors",
                    isActive
                      ? "bg-brand text-brand-foreground font-medium"
                      : "text-muted-foreground hover:bg-accent hover:text-accent-foreground",
                  )}
                >
                  <Icon size={24} className="shrink-0" />
                  <span className="min-w-0 flex-1 truncate text-left">{label}</span>
                </Link>
              }
            />
            <TooltipContent side="right">{label}</TooltipContent>
          </Tooltip>
        </TooltipProvider>
      )}
      {hasChildren && childrenOpen && (
        <ul className="mt-0.5 space-y-0.5 pl-6">
          {view.children!.map((child) => (
            <li key={child.path}>
              <NavEntry
                view={child}
                collapsed={collapsed}
                onToggleCollapsed={onToggleCollapsed}
              />
            </li>
          ))}
        </ul>
      )}
    </div>
  );

  return entryContent;
}

export function AppNavPane({ app, collapsed, onToggleCollapsed }: AppNavPaneProps) {
  const { t } = useL();
  const features = useAppFeatures();
  const isGranted = useIsGrantedChecker();
  const effectiveCollapsed = collapsed;
  /* Nodes whose feature is off, or whose permission is missing, are not shown (docs/designs/tenants-and-editions.md). */
  const views = features
    .visibleViews(app)
    .filter((view) => !view.requiredPermission || isGranted(view.requiredPermission));

  const appName = t(app.nameKey);

  return (
    <nav
      aria-label={`${appName} navigation`}
      className={cn(
        "flex min-h-0 flex-col border-r bg-surface-page py-2 transition-[width]",
        effectiveCollapsed ? "w-14" : "w-[250px]",
      )}
    >
      <div className={cn("mb-1 px-1", effectiveCollapsed && "flex justify-center")}>
        <button
          onClick={onToggleCollapsed}
          aria-label={effectiveCollapsed ? t("Shell:NavExpand") : t("Shell:NavCollapse")}
          className="flex items-center justify-center rounded-md p-2 text-muted-foreground transition-colors hover:bg-accent hover:text-accent-foreground"
        >
          {effectiveCollapsed ? <ChevronRight size={18} /> : <ChevronLeft size={18} />}
        </button>
      </div>

      {/* The tree scrolls inside the pane; the collapse toggle above stays in view. */}
      <ul className="flex min-h-0 flex-1 flex-col gap-0.5 overflow-y-auto px-1">
        {views.map((view) => (
          <li key={view.path}>
            <NavEntry
              view={view}
              collapsed={effectiveCollapsed}
              onToggleCollapsed={onToggleCollapsed}
            />
          </li>
        ))}
      </ul>
    </nav>
  );
}
