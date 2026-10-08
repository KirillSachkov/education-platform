import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook } from "@testing-library/react";
import { useRoles } from "../use-roles";
import { ROLES } from "../roles";

vi.mock("next-auth/react", () => ({
  useSession: vi.fn(),
}));

import { useSession } from "next-auth/react";
const mockUseSession = vi.mocked(useSession);

describe("useRoles", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("returns isAuthenticated: false and empty roles when no session", () => {
    mockUseSession.mockReturnValue({
      data: null,
      status: "unauthenticated",
      update: vi.fn(),
    });

    const { result } = renderHook(() => useRoles());

    expect(result.current.isAuthenticated).toBe(false);
    expect(result.current.roles).toEqual([]);
  });

  it("returns correct roles for a single role", () => {
    mockUseSession.mockReturnValue({
      data: {
        user: { roles: [ROLES.PARTICIPANT] },
        expires: "2099-01-01",
      },
      status: "authenticated",
      update: vi.fn(),
    } as ReturnType<typeof useSession>);

    const { result } = renderHook(() => useRoles());

    expect(result.current.isAuthenticated).toBe(true);
    expect(result.current.hasRole(ROLES.PARTICIPANT)).toBe(true);
    expect(result.current.hasRole(ROLES.ADMIN)).toBe(false);
  });

  it("hasAnyRole works with multiple roles", () => {
    mockUseSession.mockReturnValue({
      data: {
        user: { roles: [ROLES.AUTHOR, ROLES.MODERATOR] },
        expires: "2099-01-01",
      },
      status: "authenticated",
      update: vi.fn(),
    } as ReturnType<typeof useSession>);

    const { result } = renderHook(() => useRoles());

    expect(result.current.hasAnyRole([ROLES.ADMIN, ROLES.AUTHOR])).toBe(true);
    expect(result.current.hasAnyRole([ROLES.ADMIN, ROLES.PARTICIPANT])).toBe(
      false,
    );
  });

  it("isAtLeast checks role hierarchy correctly", () => {
    mockUseSession.mockReturnValue({
      data: {
        user: { roles: [ROLES.MODERATOR] },
        expires: "2099-01-01",
      },
      status: "authenticated",
      update: vi.fn(),
    } as ReturnType<typeof useSession>);

    const { result } = renderHook(() => useRoles());

    expect(result.current.isAtLeast(ROLES.PARTICIPANT)).toBe(true);
    expect(result.current.isAtLeast(ROLES.AUTHOR)).toBe(true);
    expect(result.current.isAtLeast(ROLES.EDITOR)).toBe(true);
    expect(result.current.isAtLeast(ROLES.MODERATOR)).toBe(true);
    expect(result.current.isAtLeast(ROLES.ADMIN)).toBe(false);
  });

  it("isAtLeast returns true for admin when checking author level", () => {
    mockUseSession.mockReturnValue({
      data: {
        user: { roles: [ROLES.ADMIN] },
        expires: "2099-01-01",
      },
      status: "authenticated",
      update: vi.fn(),
    } as ReturnType<typeof useSession>);

    const { result } = renderHook(() => useRoles());

    expect(result.current.isAtLeast(ROLES.AUTHOR)).toBe(true);
    expect(result.current.isAtLeast(ROLES.ADMIN)).toBe(true);
  });

  it("isAtLeast returns true for owner when checking admin level", () => {
    mockUseSession.mockReturnValue({
      data: {
        user: { roles: [ROLES.OWNER] },
        expires: "2099-01-01",
      },
      status: "authenticated",
      update: vi.fn(),
    } as ReturnType<typeof useSession>);

    const { result } = renderHook(() => useRoles());

    expect(result.current.isAtLeast(ROLES.ADMIN)).toBe(true);
    expect(result.current.isAtLeast(ROLES.OWNER)).toBe(true);
  });

  it("handles roles as a string (not array)", () => {
    mockUseSession.mockReturnValue({
      data: {
        user: { roles: ROLES.AUTHOR as unknown },
        expires: "2099-01-01",
      },
      status: "authenticated",
      update: vi.fn(),
    } as ReturnType<typeof useSession>);

    const { result } = renderHook(() => useRoles());

    expect(result.current.roles).toEqual([ROLES.AUTHOR]);
    expect(result.current.hasRole(ROLES.AUTHOR)).toBe(true);
  });
});
