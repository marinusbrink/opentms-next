import { APPS } from "@/app/apps.config";
import { AppPlaceholder } from "@/apps/AppPlaceholder";

export function TransportApp() {
  return <AppPlaceholder app={APPS.find((app) => app.id === "transport")!} />;
}
