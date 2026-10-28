import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useAuth } from "react-oidc-context";
import { Landing } from "@/app/Landing";

// ── Mocks ─────────────────────────────────────────────────────────────────────

vi.mock("react-oidc-context", () => ({ useAuth: vi.fn() }));

const navigate = vi.fn();
vi.mock("@tanstack/react-router", () => ({ useNavigate: () => navigate }));

vi.mock("@/lib/i18n/LocalizationProvider", () => ({ useL: () => ({ t: (key: string) => key }) }));

vi.mock("@/app/shell/FullScreenSpinner", () => ({
  FullScreenSpinner: () => <div data-testid="spinner" />,
}));

const signinRedirect = vi.fn();

function mockAuth(state: Partial<{ isLoading: boolean; isAuthenticated: boolean; activeNavigator: string; error: Error }>) {
  vi.mocked(useAuth).mockReturnValue({
    isLoading: false,
    isAuthenticated: false,
    activeNavigator: undefined,
    error: undefined,
    signinRedirect,
    ...state,
  } as unknown as ReturnType<typeof useAuth>);
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("Landing — no welcome step", () => {
  beforeEach(() => {
    signinRedirect.mockReset();
    navigate.mockReset();
  });

  it("sends a signed-out user straight to the sign-in page, without a welcome card or button", () => {
    mockAuth({});
    render(<Landing />);

    expect(signinRedirect).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("spinner")).toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("waits while the session is still loading", () => {
    mockAuth({ isLoading: true });
    render(<Landing />);

    expect(signinRedirect).not.toHaveBeenCalled();
    expect(screen.getByTestId("spinner")).toBeInTheDocument();
  });

  it("does not start a second redirect while one is under way", () => {
    mockAuth({ activeNavigator: "signinRedirect" });
    render(<Landing />);

    expect(signinRedirect).not.toHaveBeenCalled();
  });

  it("takes a signed-in user to the dashboard", () => {
    mockAuth({ isAuthenticated: true });
    render(<Landing />);

    expect(navigate).toHaveBeenCalledWith({ to: "/dashboard" });
    expect(signinRedirect).not.toHaveBeenCalled();
  });

  it("on an auth error shows the message and a sign-in button instead of redirecting again", () => {
    mockAuth({ error: new Error("Sign-in failed") });
    render(<Landing />);

    expect(signinRedirect).not.toHaveBeenCalled();
    expect(screen.getByText("Sign-in failed")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Shell:SignIn" }));
    expect(signinRedirect).toHaveBeenCalledTimes(1);
  });
});
