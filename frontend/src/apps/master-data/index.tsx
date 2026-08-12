import { APPS } from "@/app/apps.config";
import { AppPlaceholder } from "@/apps/AppPlaceholder";

export function MasterDataApp() {
  return <AppPlaceholder app={APPS.find((app) => app.id === "master-data")!} />;
}
