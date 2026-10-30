import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { RouterProvider } from "@tanstack/react-router";
import "./index.css";
import { Providers } from "@/app/providers";
import { router } from "@/app/router";
import { settleSessionSwitch } from "@/auth/session-switch";
import { discardUnknownStoredTenant } from "@/auth/stale-tenant";

/* The stored tenant is settled before the first render, which is where the OIDC configuration and the API client read
 * it: a refused "Log in as" puts the previous tenant back, then a tenant that no longer exists is forgotten. */
settleSessionSwitch();
void discardUnknownStoredTenant().then(() => {
  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <Providers>
        <RouterProvider router={router} />
      </Providers>
    </StrictMode>,
  );
});
