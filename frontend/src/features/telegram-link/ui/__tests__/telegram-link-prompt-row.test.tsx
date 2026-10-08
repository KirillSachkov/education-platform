import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const { linkTelegram } = vi.hoisted(() => ({ linkTelegram: vi.fn() }));

vi.mock("@tanstack/react-query", () => ({ useQuery: vi.fn() }));
vi.mock("@/entities/profile", () => ({
  profileQueryOptions: { getMyProfileOptions: () => ({}) },
}));
vi.mock("../../model/use-telegram-link", () => ({
  useTelegramLink: () => ({ linkTelegram, isPending: false }),
}));

import { useQuery } from "@tanstack/react-query";
import { TelegramLinkPromptRow } from "../telegram-link-prompt-row";

const mockUseQuery = vi.mocked(useQuery);

function mockProfile(data: { hasTelegramLinked: boolean } | undefined) {
  mockUseQuery.mockReturnValue({ data } as unknown as ReturnType<typeof useQuery>);
}

describe("TelegramLinkPromptRow", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders nothing while the profile is still loading", () => {
    mockProfile(undefined);
    const { container } = render(<TelegramLinkPromptRow />);
    expect(container.innerHTML).toBe("");
  });

  it("renders nothing when Telegram is already linked", () => {
    mockProfile({ hasTelegramLinked: true });
    const { container } = render(<TelegramLinkPromptRow />);
    expect(container.innerHTML).toBe("");
  });

  it("shows a one-click CTA and links on click when not linked", async () => {
    mockProfile({ hasTelegramLinked: false });
    render(<TelegramLinkPromptRow />);

    const button = screen.getByRole("button", { name: /привязать telegram/i });
    await userEvent.click(button);

    expect(linkTelegram).toHaveBeenCalledOnce();
  });
});
