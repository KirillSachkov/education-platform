import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const useSession = vi.hoisted(() => vi.fn());
const trackAuthView = vi.hoisted(() => vi.fn());
const trackAnonymousView = vi.hoisted(() => vi.fn());
const trackGrowthEvent = vi.hoisted(() => vi.fn());

vi.mock("next-auth/react", () => ({ useSession }));
vi.mock("../api", () => ({ trackMaterialViewApi: { trackAuthView, trackAnonymousView } }));
vi.mock("@/shared/analytics", () => ({ trackGrowthEvent }));
vi.mock("@/shared/lib/anonymous-id", () => ({ getOrCreateAnonymousId: () => "anon-1" }));

import { useTrackMaterialView } from "../use-track-material-view";

describe("useTrackMaterialView growth activation", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    trackAuthView.mockResolvedValue(undefined);
    trackAnonymousView.mockResolvedValue(undefined);
  });

  it("records first material start only after an authenticated view succeeds", async () => {
    useSession.mockReturnValue({ status: "authenticated" });

    renderHook(() => {
      useTrackMaterialView({ materialId: "material-1", courseId: "course-1" });
    });

    await waitFor(() => {
      expect(trackAuthView).toHaveBeenCalledWith("material-1");
    });
    expect(trackGrowthEvent).toHaveBeenCalledWith(
      {
        name: "first_material_started",
        properties: { material_id: "material-1", course_id: "course-1" },
      },
      { once: "activation:first-material-started" },
    );
  });

  it("does not record paid-user activation for an anonymous view", async () => {
    useSession.mockReturnValue({ status: "unauthenticated" });

    renderHook(() => {
      useTrackMaterialView({ materialId: "material-1" });
    });

    await waitFor(() => {
      expect(trackAnonymousView).toHaveBeenCalledWith("material-1", "anon-1");
    });
    expect(trackGrowthEvent).not.toHaveBeenCalled();
  });

  it("does not consume course activation for an authenticated standalone material", async () => {
    useSession.mockReturnValue({ status: "authenticated" });

    renderHook(() => {
      useTrackMaterialView({ materialId: "material-1" });
    });

    await waitFor(() => {
      expect(trackAuthView).toHaveBeenCalledWith("material-1");
    });
    expect(trackGrowthEvent).not.toHaveBeenCalled();
  });
});
