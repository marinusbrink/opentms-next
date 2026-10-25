import { ViewPlaceholder } from "@/apps/AppPlaceholder";
import { useL } from "@/lib/i18n/LocalizationProvider";

export interface TemplatePageProps {
  id: string;
}

/* Design 61, "Templates": the order editor in template mode (Name instead of number, the Schedule section), reached
 * through useOpenRecord. The screen arrives with the Orders backend (design 58); until then the page skeleton. */
export function TemplatePage({ id }: TemplatePageProps) {
  const { t } = useL();
  return (
    <ViewPlaceholder permission="Orders.TransportOrderTemplates" feature="Apps.Transport.Orders.Templates" heading={t("App:Transport.Templates")}>
      <div data-slot="template-page" data-record-id={id} />
    </ViewPlaceholder>
  );
}
