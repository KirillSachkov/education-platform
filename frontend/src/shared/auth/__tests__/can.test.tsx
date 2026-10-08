import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { Can } from "../can";
import { ROLES } from "../roles";

vi.mock("next-auth/react", () => ({
  useSession: vi.fn(),
}));

import { useSession } from "next-auth/react";
const mockUseSession = vi.mocked(useSession);

function mockSession(roles: string[]) {
  mockUseSession.mockReturnValue({
    data: {
      user: { roles },
      expires: "2099-01-01",
    },
    status: "authenticated",
    update: vi.fn(),
  } as ReturnType<typeof useSession>);
}

function mockNoSession() {
  mockUseSession.mockReturnValue({
    data: null,
    status: "unauthenticated",
    update: vi.fn(),
  });
}

describe("Can", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders children when user has matching role", () => {
    mockSession([ROLES.ADMIN]);
    render(
      <Can role={ROLES.ADMIN}>
        <span>Admin content</span>
      </Can>,
    );
    expect(screen.getByText("Admin content")).toBeInTheDocument();
  });

  it("renders nothing (null) when user lacks role and no fallback", () => {
    mockSession([ROLES.PARTICIPANT]);
    const { container } = render(
      <Can role={ROLES.ADMIN}>
        <span>Admin content</span>
      </Can>,
    );
    expect(screen.queryByText("Admin content")).not.toBeInTheDocument();
    expect(container.innerHTML).toBe("");
  });

  it("renders fallback when user lacks role", () => {
    mockSession([ROLES.PARTICIPANT]);
    render(
      <Can role={ROLES.ADMIN} fallback={<span>No access</span>}>
        <span>Admin content</span>
      </Can>,
    );
    expect(screen.queryByText("Admin content")).not.toBeInTheDocument();
    expect(screen.getByText("No access")).toBeInTheDocument();
  });

  it("works with roles prop (array)", () => {
    mockSession([ROLES.MODERATOR]);
    render(
      <Can roles={[ROLES.MODERATOR, ROLES.ADMIN]}>
        <span>Mod or admin</span>
      </Can>,
    );
    expect(screen.getByText("Mod or admin")).toBeInTheDocument();
  });

  it("works with atLeast prop", () => {
    mockSession([ROLES.ADMIN]);
    render(
      <Can atLeast={ROLES.AUTHOR}>
        <span>Author or higher</span>
      </Can>,
    );
    expect(screen.getByText("Author or higher")).toBeInTheDocument();
  });

  it("works with atLeast prop for owner above admin", () => {
    mockSession([ROLES.OWNER]);
    render(
      <Can atLeast={ROLES.ADMIN}>
        <span>Admin or owner</span>
      </Can>,
    );
    expect(screen.getByText("Admin or owner")).toBeInTheDocument();
  });

  it("hides content with atLeast when role is too low", () => {
    mockSession([ROLES.PARTICIPANT]);
    render(
      <Can atLeast={ROLES.AUTHOR}>
        <span>Author or higher</span>
      </Can>,
    );
    expect(screen.queryByText("Author or higher")).not.toBeInTheDocument();
  });

  it("renders nothing when no session exists", () => {
    mockNoSession();
    const { container } = render(
      <Can role={ROLES.ADMIN}>
        <span>Admin content</span>
      </Can>,
    );
    expect(container.innerHTML).toBe("");
  });
});
