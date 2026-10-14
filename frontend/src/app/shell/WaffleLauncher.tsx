import { useState } from "react";
import { Link } from "@tanstack/react-router";
import { Grip } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { APPS } from "@/app/apps.config";
import { useAppFeatures } from "@/app/app-features";
import { useIsGrantedChecker } from "@/lib/auth/permissions";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The Office-365-style app launcher: waffle button top-left, opening a panel of
 * app tiles. The tile list comes exclusively from apps.config.ts; an app with a
 * requiredPermission shows only when it is granted (UI-PERM-002: hide, never disable), and an app whose
 * feature is off, or whose navigation nodes are all off, is not shown ("App features", tenants-and-editions.md). */
/** No requirement, or at least one of the listed permissions granted. */
function hasAnyPermission(required: string | readonly string[] | undefined, isGranted: (policy: string) => boolean) {
  if (required == null) return true;
  return (typeof required === "string" ? [required] : required).some(isGranted);
}

export function WaffleLauncher() {
  const { t } = useL();
  const [open, setOpen] = useState(false);
  const isGranted = useIsGrantedChecker();
  const features = useAppFeatures();
  const apps = APPS.filter(
    (app) => hasAnyPermission(app.requiredPermission, isGranted) && features.isAppEnabled(app),
  );

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            variant="ghost"
            size="icon"
            aria-label={t("Shell:AppLauncher")}
            className="size-12 rounded-none text-white hover:bg-white/15 hover:text-white"
          >
            <Grip className="size-5" />
          </Button>
        }
      />
      <PopoverContent align="start" sideOffset={0} className="w-80 p-3">
        <p className="px-1 pb-2 text-sm font-semibold text-muted-foreground">{t("Shell:Apps")}</p>
        <div className="grid grid-cols-3 gap-2">
          {apps.map((app) => (
            <Link
              key={app.id}
              to={app.path}
              onClick={() => setOpen(false)}
              className="flex flex-col items-center gap-2 rounded-md p-3 text-center hover:bg-accent"
            >
              <span
                className={`flex size-10 items-center justify-center rounded-lg text-white ${app.tileClass}`}
              >
                <app.icon className="size-5" />
              </span>
              <span className="text-xs leading-tight">{t(app.nameKey)}</span>
            </Link>
          ))}
        </div>
      </PopoverContent>
    </Popover>
  );
}
