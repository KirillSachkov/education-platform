import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useUploadPreview } from "../use-upload-preview";

const upload = vi.fn();
vi.mock("../use-file-upload", () => ({
  useFileUpload: () => ({ upload }),
}));
vi.mock("../api", () => ({
  fileApi: { deleteFile: vi.fn() },
}));

const downscaleImage = vi.fn();
vi.mock("@/shared/lib/downscale-image", () => ({
  downscaleImage: (file: File) => downscaleImage(file),
}));

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

function config() {
  return {
    usageType: "course_preview" as const,
    entityType: "course" as never,
    entityId: "course-1",
    invalidateKey: ["courses"] as const,
  };
}

describe("useUploadPreview", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    upload.mockResolvedValue({ assetId: "asset-9", contentUrl: "/api/files/asset-9/content" });
  });

  it("downscales before upload and exposes a blob: preview", async () => {
    const original = new File(["raw"], "course-cover.png", { type: "image/png" });
    const downscaled = new File(["small"], "course-cover.webp", { type: "image/webp" });
    downscaleImage.mockResolvedValue(downscaled);
    const createObjectURL = vi
      .spyOn(URL, "createObjectURL")
      .mockReturnValue("blob:http://localhost/course-1");

    const { result } = renderHook(() => useUploadPreview(config()), { wrapper });

    act(() => result.current.upload(original));
    await waitFor(() => expect(result.current.uploadedAssetId).toBe("asset-9"));

    expect(downscaleImage).toHaveBeenCalledWith(original);
    expect(upload).toHaveBeenCalledWith(downscaled);
    expect(createObjectURL).toHaveBeenCalledWith(downscaled);
    expect(result.current.previewUrl).toBe("blob:http://localhost/course-1");
  });

  it("revokes the blob: URL on unmount", async () => {
    const original = new File(["raw"], "c.png", { type: "image/png" });
    downscaleImage.mockResolvedValue(original);
    vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:http://localhost/course-2");
    const revoke = vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => {});

    const { result, unmount } = renderHook(() => useUploadPreview(config()), { wrapper });
    act(() => result.current.upload(original));
    await waitFor(() => expect(result.current.previewUrl).toBe("blob:http://localhost/course-2"));

    unmount();
    expect(revoke).toHaveBeenCalledWith("blob:http://localhost/course-2");
  });
});
