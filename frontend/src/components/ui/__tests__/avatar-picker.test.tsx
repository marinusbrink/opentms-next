import { fireEvent, render, screen } from "@testing-library/react";
import { beforeAll, describe, expect, it, vi } from "vitest";

import { AvatarPicker } from "@/components/ui/avatar-picker";

/* Risk class: Medium — the staged value decides whether Save uploads, removes or leaves the picture. */

vi.mock("@/lib/i18n/LocalizationProvider", () => ({
  useL: () => ({ t: (k: string) => k, culture: "en", isLoading: false }),
}));

beforeAll(() => {
  vi.stubGlobal("URL", { ...URL, createObjectURL: vi.fn(() => "blob:preview"), revokeObjectURL: vi.fn() });
});

const png = new File(["x"], "me.png", { type: "image/png" });

describe("AvatarPicker", () => {
  it("shows initials without a picture and stages a file chosen through the chooser", () => {
    const onChange = vi.fn();
    render(<AvatarPicker currentUrl={null} pending={undefined} onChange={onChange} initials="MB" label="Profile picture" />);

    expect(screen.getByRole("button", { name: "Profile picture" })).toHaveTextContent("MB");
    fireEvent.change(screen.getByLabelText("Administration:ChangePicture"), { target: { files: [png] } });
    expect(onChange).toHaveBeenCalledWith(png);
  });

  it("stages a dropped image and ignores a dropped file of another type", () => {
    const onChange = vi.fn();
    render(<AvatarPicker currentUrl={null} pending={undefined} onChange={onChange} initials="MB" label="Profile picture" />);
    const target = screen.getByRole("button", { name: "Profile picture" });

    fireEvent.dragOver(target, { dataTransfer: { types: ["text/plain"], files: [] } });
    expect(target).not.toHaveAttribute("data-drag-over");
    fireEvent.dragOver(target, { dataTransfer: { types: ["Files"], files: [] } });
    expect(target).toHaveAttribute("data-drag-over", "true");
    fireEvent.drop(target, { dataTransfer: { files: [new File(["x"], "a.txt", { type: "text/plain" })] } });
    expect(onChange).not.toHaveBeenCalled();
    fireEvent.drop(target, { dataTransfer: { files: [png] } });
    expect(onChange).toHaveBeenCalledWith(png);
    expect(target).not.toHaveAttribute("data-drag-over");
  });

  it("offers Change and Remove in the context menu; Remove stages removal of a stored picture", async () => {
    const onChange = vi.fn();
    const { rerender } = render(
      <AvatarPicker currentUrl="blob:stored" pending={undefined} onChange={onChange} initials="MB" label="Profile picture" />,
    );
    const target = screen.getByRole("button", { name: "Profile picture" });
    expect(target.querySelector("img")).toHaveAttribute("src", "blob:stored");

    fireEvent.contextMenu(target);
    const items = await screen.findAllByRole("menuitem");
    expect(items.map((i) => i.textContent)).toEqual(["Administration:ChangePicture", "Administration:RemovePicture"]);
    fireEvent.click(items[1]);
    expect(onChange).toHaveBeenLastCalledWith(null);

    rerender(<AvatarPicker currentUrl="blob:stored" pending={null} onChange={onChange} initials="MB" label="Profile picture" />);
    expect(screen.getByRole("button", { name: "Profile picture" })).toHaveTextContent("MB");

    rerender(<AvatarPicker currentUrl={null} pending={undefined} onChange={onChange} initials="MB" label="Profile picture" />);
    fireEvent.contextMenu(screen.getByRole("button", { name: "Profile picture" }));
    expect((await screen.findAllByRole("menuitem")).map((i) => i.textContent)).toEqual(["Administration:ChangePicture"]);
  });

  it("does nothing when disabled", () => {
    const onChange = vi.fn();
    render(<AvatarPicker currentUrl={null} pending={undefined} onChange={onChange} initials="MB" label="Profile picture" disabled />);
    const target = screen.getByRole("button", { name: "Profile picture" });
    expect(target).toBeDisabled();
    fireEvent.drop(target, { dataTransfer: { files: [png] } });
    expect(onChange).not.toHaveBeenCalled();
  });
});
