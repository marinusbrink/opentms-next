import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";

/* "Change profile picture" (docs/designs/63-user-menu.md): the change is staged in the AvatarPicker and saved with the
 * primary: a file uploads, "remove" deletes, nothing staged keeps Save disabled. Risk class: Medium. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false, setUserLanguage: vi.fn() }),
}));

let currentUrl: string | null = "blob:stored";
const setMutate = vi.fn();
const deleteMutate = vi.fn();
vi.mock("@/domains/platform/my-profile", () => ({
  useMyProfile: () => ({ data: { userName: "jdoe", name: "Jan", surname: "de Vries", hasProfilePicture: currentUrl != null } }),
  useMyProfilePictureUrl: () => currentUrl,
  useSetMyProfilePicture: () => ({ mutateAsync: setMutate, isPending: false }),
  useDeleteMyProfilePicture: () => ({ mutateAsync: deleteMutate, isPending: false }),
}));

const toastSuccess = vi.fn();
vi.mock("sonner", () => ({ toast: { success: (...a: unknown[]) => toastSuccess(...a), error: vi.fn() } }));

class MockResizeObserver {
  observe() {}
  disconnect() {}
  unobserve() {}
}
beforeAll(() => {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
  vi.stubGlobal("URL", { ...URL, createObjectURL: vi.fn(() => "blob:preview"), revokeObjectURL: vi.fn() });
});

import { ChangeProfilePictureDialog } from "@/app/shell/user-menu/ChangeProfilePictureDialog";

const png = new File(["x"], "me.png", { type: "image/png" });
const save = () => screen.getByRole("button", { name: "Save" });

beforeEach(() => {
  currentUrl = "blob:stored";
  setMutate.mockReset();
  deleteMutate.mockReset();
  toastSuccess.mockReset();
});

describe("ChangeProfilePictureDialog", () => {
  it("keeps Save disabled until a change is staged", () => {
    render(<ChangeProfilePictureDialog open onClose={() => {}} />);
    expect(save()).toBeDisabled();
  });

  it("uploads a chosen picture, toasts and closes", async () => {
    setMutate.mockResolvedValue(undefined);
    const onClose = vi.fn();
    render(<ChangeProfilePictureDialog open onClose={onClose} />);
    fireEvent.change(screen.getByLabelText("Administration:ChangePicture"), { target: { files: [png] } });
    expect(save()).toBeEnabled();
    await act(async () => fireEvent.click(save()));

    expect(setMutate).toHaveBeenCalledExactlyOnceWith(png);
    expect(deleteMutate).not.toHaveBeenCalled();
    expect(toastSuccess).toHaveBeenCalledWith("UserMenu:ProfilePictureSaved");
    expect(onClose).toHaveBeenCalled();
  });

  it("removes the stored picture when Remove is staged", async () => {
    deleteMutate.mockResolvedValue(undefined);
    render(<ChangeProfilePictureDialog open onClose={() => {}} />);
    fireEvent.contextMenu(screen.getByRole("button", { name: "UserMenu:ProfilePicture" }));
    const remove = (await screen.findAllByRole("menuitem")).find((i) => i.textContent === "Administration:RemovePicture")!;
    fireEvent.click(remove);
    await act(async () => fireEvent.click(save()));

    expect(deleteMutate).toHaveBeenCalledOnce();
    expect(setMutate).not.toHaveBeenCalled();
  });

  it("shows initials without a stored picture", () => {
    currentUrl = null;
    render(<ChangeProfilePictureDialog open onClose={() => {}} />);
    expect(screen.getByRole("button", { name: "UserMenu:ProfilePicture" })).toHaveTextContent("JD");
  });

  it("shows an upload error inline and stays open", async () => {
    setMutate.mockRejectedValue({ error: { message: "The file is too large." } });
    const onClose = vi.fn();
    render(<ChangeProfilePictureDialog open onClose={onClose} />);
    fireEvent.change(screen.getByLabelText("Administration:ChangePicture"), { target: { files: [png] } });
    await act(async () => fireEvent.click(save()));

    expect(screen.getByRole("alert")).toHaveTextContent("The file is too large.");
    expect(onClose).not.toHaveBeenCalled();
  });
});
