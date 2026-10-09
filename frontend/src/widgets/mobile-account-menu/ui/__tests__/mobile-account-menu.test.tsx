import { render, screen } from "@testing-library/react";
import { useSession } from "next-auth/react";
import { describe, expect, it, vi } from "vitest";
import { ROLES } from "@/shared/auth";
import { routes } from "@/shared/config/routes";
import { MobileAccountMenu } from "../mobile-account-menu";

vi.mock("next-auth/react", () => ({ useSession: vi.fn(), signOut: vi.fn() }));

describe("retained review settings in the mobile account menu", () => {
  it.each([ROLES.ADMIN, ROLES.OWNER])("keeps settings reachable for %s", (role) => {
    vi.mocked(useSession).mockReturnValue({
      data: { user: { roles: [role] }, expires: "2099-01-01" },
      status: "authenticated",
      update: vi.fn(),
    });
    render(<MobileAccountMenu />);
    expect(screen.getByRole("link", { name: "AI модели" })).toHaveAttribute(
      "href",
      routes.adminAiModels,
    );
    expect(screen.queryByRole("link", { name: "AI usage" })).not.toBeInTheDocument();
  });

  it.each([ROLES.PARTICIPANT, ROLES.AUTHOR, ROLES.MODERATOR])(
    "keeps admin settings hidden for %s",
    (role) => {
      vi.mocked(useSession).mockReturnValue({
        data: { user: { roles: [role] }, expires: "2099-01-01" },
        status: "authenticated",
        update: vi.fn(),
      });
      render(<MobileAccountMenu />);
      expect(screen.queryByRole("link", { name: "AI модели" })).not.toBeInTheDocument();
    },
  );
});
