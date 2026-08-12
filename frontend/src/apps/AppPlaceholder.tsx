import type { AppDefinition } from "@/app/apps.config";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Empty authenticated page for a stubbed app. Real features arrive per approved
 * design through the department pipeline — never by growing this placeholder. */
export function AppPlaceholder({ app }: { app: AppDefinition }) {
  const { t } = useL();

  return (
    <div className="mx-auto mt-16 flex max-w-md flex-col items-center text-center">
      <span className={`flex size-16 items-center justify-center rounded-2xl text-white ${app.tileClass}`}>
        <app.icon className="size-8" />
      </span>
      <h1 className="mt-6 text-xl font-semibold">{t(app.nameKey)}</h1>
      <p className="mt-2 text-sm font-medium text-muted-foreground">{t("Shell:EmptyAppTitle")}</p>
      <p className="mt-1 text-sm text-muted-foreground">{t("Shell:EmptyAppText")}</p>
      <p className="mt-6 text-xs text-muted-foreground">
        {t("Shell:ReadsFrom")}:{" "}
        {app.domains.map((domain) => (
          <span key={domain} className="mx-1 rounded bg-neutral-200 px-1.5 py-0.5 font-mono">
            {domain}
          </span>
        ))}
      </p>
    </div>
  );
}
