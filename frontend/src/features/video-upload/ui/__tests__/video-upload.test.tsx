import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { VideoUpload } from "../video-upload";

const mocks = vi.hoisted(() => ({
  upload: vi.fn(),
  onUploadComplete: null as ((assetId: string) => void) | null,
  assetPolling: vi.fn(),
}));

vi.mock("../../model/use-video-upload", () => ({
  useVideoUpload: (options: { onComplete: (assetId: string) => void }) => {
    mocks.onUploadComplete = options.onComplete;
    return {
      upload: mocks.upload,
      cancel: vi.fn(),
      reset: vi.fn(),
      uploadState: {
        status: "idle",
        progress: 0,
        uploadedBytes: 0,
        totalBytes: 0,
      },
    };
  },
}));

vi.mock("../../model/use-video-polling", () => ({
  useVideoPolling: () => ({ video: null, isPolling: false }),
}));

vi.mock("../../model/use-video-asset-polling", () => ({
  useVideoAssetPolling: (options: { assetId: string | null; initialVideo: unknown }) => {
    mocks.assetPolling(options);
    return { video: options.initialVideo, isPolling: Boolean(options.assetId) };
  },
}));

vi.mock("../../model/use-attach-external-video", () => ({
  useAttachExternalVideo: () => ({
    attach: vi.fn(),
    isLoading: false,
    error: null,
    data: null,
  }),
}));

vi.mock("@/entities/video", async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>();
  return {
    ...actual,
    useDeleteVideo: () => ({ deleteVideo: vi.fn(), isDeleting: false }),
  };
});

describe("VideoUpload", () => {
  beforeEach(() => {
    mocks.upload.mockClear();
    mocks.assetPolling.mockClear();
    mocks.onUploadComplete = null;
  });

  it("starts upload when a video file is pasted onto the empty tile", async () => {
    mocks.upload.mockResolvedValueOnce(undefined);
    const file = new File(["video"], "course.mp4", { type: "video/mp4" });

    render(<VideoUpload entityType="material" video={null} />);

    fireEvent.paste(screen.getByText("Загрузить видео"), {
      clipboardData: { files: [file] },
    });

    await waitFor(() => expect(mocks.upload).toHaveBeenCalledWith(file));
  });

  it("starts upload when a video file is dropped onto the empty tile", async () => {
    mocks.upload.mockResolvedValueOnce(undefined);
    const file = new File(["video"], "course.webm", { type: "video/webm" });

    render(<VideoUpload entityType="material" video={null} />);

    fireEvent.drop(screen.getByText("Загрузить видео"), {
      dataTransfer: { files: [file] },
    });

    await waitFor(() => expect(mocks.upload).toHaveBeenCalledWith(file));
  });

  it("polls a fresh edit-mode upload by exact asset id", () => {
    render(
      <VideoUpload
        entityId="material-1"
        entityType="material"
        video={{
          id: "old-video",
          videoId: "old-video",
          status: "ready",
          thumbnailUrl: null,
          duration: null,
          isOwnedStorage: true,
        }}
      />,
    );

    act(() => mocks.onUploadComplete?.("new-video"));

    expect(mocks.assetPolling).toHaveBeenLastCalledWith(
      expect.objectContaining({ assetId: "new-video" }),
    );
  });
});
