import { useEffect } from "react";
import { useNavigate } from "@tanstack/react-router";
import { useAuth } from "react-oidc-context";
import { Button } from "@/components/ui/button";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { FullScreenSpinner } from "@/app/shell/FullScreenSpinner";

export function Landing() {
  const auth = useAuth();
  const { t } = useL();
  const navigate = useNavigate();

  useEffect(() => {
    if (auth.isAuthenticated) {
      void navigate({ to: "/dashboard" });
    }
  }, [auth.isAuthenticated, navigate]);

  if (auth.isLoading || auth.isAuthenticated) {
    return <FullScreenSpinner />;
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-neutral-100">
      <div className="w-full max-w-sm rounded-xl border bg-white p-8 text-center shadow-sm">
        <span className="mx-auto flex size-12 items-center justify-center rounded-xl bg-[#0f6cbd] text-lg font-bold text-white">
          {t("AppName").slice(0, 1)}
        </span>
        <h1 className="mt-4 text-xl font-semibold">{t("Shell:WelcomeTitle")}</h1>
        <p className="mt-1 text-sm text-muted-foreground">{t("Shell:WelcomeText")}</p>
        <Button
          className="mt-6 w-full bg-[#0f6cbd] hover:bg-[#0f6cbd]/90"
          onClick={() => void auth.signinRedirect()}
        >
          {t("Shell:SignIn")}
        </Button>
      </div>
    </div>
  );
}
