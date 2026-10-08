import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useUploadMaterialPreview } from "../use-upload-material-preview";

const upload = vi.fn();

vi.mock("@/entities/file", () => ({
  useFileUpload: () => ({ upload }),
  fileApi: { deleteFile: vi.fn() },
}));

vi.mock("@/entities/course", () => ({
  invalidateEducationContent: vi.fn().mockResolvedValue(undefined),
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

describe("useUploadMaterialPreview", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    upload.mockResolvedValue({ assetId: "asset-1", contentUrl: "/api/files/asset-1/content" });
  });

  it("downscales the file before uploading and shows a blob: preview on success", async () => {
    const original = new File(["raw"], "cover.jpg", { type: "image/jpeg" });
    const downscaled = new File(["small"], "cover.webp", { type: "image/webp" });
    downscaleImage.mockResolvedValue(downscaled);

    const createObjectURL = vi
      .spyOn(URL, "createObjectURL")
      .mockReturnValue("blob:http://localhost/material-1");

    const { result } = renderHook(() => useUploadMaterialPreview({ draftId: "draft-1" }), {
      wrapper,
    });

    act(() => result.current.upload(original));

    await waitFor(() => expect(result.current.uploadedAssetId).toBe("asset-1"));

    // Downscale ran on the original, upload received the downscaled file.
    expect(downscaleImage).toHaveBeenCalledWith(original);
    expect(upload).toHaveBeenCalledWith(downscaled);
    // Preview is the local blob, not the storage contentUrl.
    expect(createObjectURL).toHaveBeenCalledWith(downscaled);
    expect(result.current.previewUrl).toBe("blob:http://localhost/material-1");
  });

  it("revokes the blob: preview URL on unmount", async () => {
    const original = new File(["raw"], "cover.jpg", { type: "image/jpeg" });
    downscaleImage.mockResolvedValue(original);
    vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:http://localhost/material-2");
    const revoke = vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => {});

    const { result, unmount } = renderHook(
      () => useUploadMaterialPreview({ draftId: "draft-1" }),
      { wrapper },
    );

    act(() => result.current.upload(original));
    await waitFor(() => expect(result.current.previewUrl).toBe("blob:http://localhost/material-2"));

    unmount();
    expect(revoke).toHaveBeenCalledWith("blob:http://localhost/material-2");
  });
});
