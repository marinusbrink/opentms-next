import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { CopyButton } from "@/components/ui/copy-button";

/* CopyButton (design 72: the API client's one-time secret dialog; UI-ACT-006/007, UI-FB-003): a ghost icon button with
 * its accessible name and tooltip; the value goes to the clipboard and the icon turns into a check for 1.5 s; a refused
 * clipboard changes nothing and throws nothing. Risk class: Medium (the secret is shown once: a copy that silently
 * failed would lose it). */

let writeText: ReturnType<typeof vi.fn>;

beforeEach(() => {
  writeText = vi.fn().mockResolvedValue(undefined);
  Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
});

afterEach(() => {
  vi.useRealTimers();
});

const button = () => screen.getByRole("button", { name: "Copy client secret" });
const isCheck = () => button().querySelector("svg")?.getAttribute("class")?.includes("lucide-check") ?? false;
const click = async () =>
  act(async () => {
    fireEvent.click(button());
    await Promise.resolve();
  });

describe("CopyButton", () => {
  it("is a button named by its label", () => {
    render(<CopyButton value="s3cr3t" label="Copy client secret" />);

    expect(button()).toHaveAttribute("type", "button");
    expect(button()).toHaveAttribute("aria-label", "Copy client secret");
    expect(isCheck()).toBe(false);
  });

  it("copies the exact value and shows a check for a moment", async () => {
    vi.useFakeTimers();
    render(<CopyButton value="otms_ABC  secret-with-spaces " label="Copy client secret" />);

    await click();

    expect(writeText).toHaveBeenCalledTimes(1);
    expect(writeText).toHaveBeenCalledWith("otms_ABC  secret-with-spaces ");
    expect(isCheck()).toBe(true);

    await act(async () => {
      vi.advanceTimersByTime(1499);
    });
    expect(isCheck()).toBe(true);
    await act(async () => {
      vi.advanceTimersByTime(1);
    });
    expect(isCheck()).toBe(false);
  });

  it("a second copy restarts the moment", async () => {
    vi.useFakeTimers();
    render(<CopyButton value="v" label="Copy client secret" />);

    await click();
    await act(async () => {
      vi.advanceTimersByTime(1000);
    });
    await click();
    await act(async () => {
      vi.advanceTimersByTime(1000);
    });

    expect(isCheck()).toBe(true);
    expect(writeText).toHaveBeenCalledTimes(2);
  });

  it("a refused clipboard changes nothing and throws nothing", async () => {
    writeText.mockRejectedValue(new DOMException("denied", "NotAllowedError"));
    render(<CopyButton value="v" label="Copy client secret" />);

    await click();

    expect(writeText).toHaveBeenCalled();
    expect(isCheck()).toBe(false);
  });

  it("clears its timer when it unmounts", async () => {
    vi.useFakeTimers();
    const { unmount } = render(<CopyButton value="v" label="Copy client secret" />);
    await click();

    unmount();

    expect(vi.getTimerCount()).toBe(0);
  });
});
