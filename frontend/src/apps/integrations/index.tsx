import { APPS } from "@/app/apps.config";
import { AppPlaceholder } from "@/apps/AppPlaceholder";

export function IntegrationsApp() {
  return <AppPlaceholder app={APPS.find((app) => app.id === "integrations")!} />;
}
