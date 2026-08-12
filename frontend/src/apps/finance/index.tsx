import { APPS } from "@/app/apps.config";
import { AppPlaceholder } from "@/apps/AppPlaceholder";

export function FinanceApp() {
  return <AppPlaceholder app={APPS.find((app) => app.id === "finance")!} />;
}
