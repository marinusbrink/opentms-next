import { APPS } from "@/app/apps.config";
import { AppPlaceholder } from "@/apps/AppPlaceholder";

export function PlanboardApp() {
  return <AppPlaceholder app={APPS.find((app) => app.id === "planboard")!} />;
}
