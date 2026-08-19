import { useState } from "react";
import { Link, useLocation } from "@tanstack/react-router";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { type AppDefinition, type AppView } from "@/app/apps.config";
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

function NavEntry({ view, collapsed, onToggleCollapsed }: NavEntryProps) {
  const { t } = useL();
  const location = useLocation();
  const [childrenOpen, setChildrenOpen] = useState(false);

  const isActive = location.pathname === view.path || location.pathname.startsWith(`${view.path}/`);
  const hasChildren = view.children != null && view.children.length > 0;
  const label = t(view.nameKey);
  const Icon = view.icon;

  function handleClick() {
    if (hasChildren) {
      if (collapsed) {
        onToggleCollapsed();
      } else {
        setChildrenOpen((v) => !v);
      }
    }
  }

  const iconRailClassName = cn(
    "flex w-full items-center justify-center rounded-md py-2 transition-colors hover:bg-accent hover:text-accent-foreground",
    isActive ? "text-foreground" : "text-muted-foreground",
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
      {isActive && !hasChildren && (
        <span className="absolute inset-y-0 left-0 w-0.5 rounded-full bg-primary" aria-hidden />
      )}
      {hasChildren ? (
        <button
          onClick={handleClick}
          className={cn(
            "flex w-full items-center gap-3 rounded-md py-2 pl-3 pr-2 text-sm transition-colors hover:bg-accent hover:text-accent-foreground",
            isActive ? "text-foreground font-medium" : "text-muted-foreground",
          )}
        >
          <Icon size={24} className="shrink-0" />
          <span className="min-w-0 flex-1 truncate">{label}</span>
          <ChevronRight
            size={16}
            className={cn("shrink-0 transition-transform", childrenOpen && "rotate-90")}
          />
        </button>
      ) : (
        <TooltipProvider>
          <Tooltip>
            <TooltipTrigger
              render={
                <Link
                  to={view.path}
                  aria-current={isActive ? "page" : undefined}
                  className={cn(
                    "flex w-full items-center gap-3 rounded-md py-2 pl-3 pr-2 text-sm transition-colors hover:bg-accent hover:text-accent-foreground",
                    isActive ? "text-foreground font-medium" : "text-muted-foreground",
                  )}
                >
                  <Icon size={24} className="shrink-0" />
                  <span className="min-w-0 flex-1 truncate">{label}</span>
                </Link>
              }
            />
            <TooltipContent side="right">{label}</TooltipContent>
          </Tooltip>
        </TooltipProvider>
      )}
      {hasChildren && childrenOpen && (
        <ul className="mt-0.5 space-y-0.5 pl-8">
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
  const effectiveCollapsed = collapsed;

  const appName = t(app.nameKey);

  return (
    <nav
      aria-label={`${appName} navigation`}
      className={cn(
        "flex flex-col border-r bg-background py-2 transition-[width]",
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

      <ul className="flex flex-col gap-0.5 px-1">
        {app.views?.map((view) => (
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
