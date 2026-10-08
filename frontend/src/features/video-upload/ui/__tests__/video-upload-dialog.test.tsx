import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { VideoUploadDialog } from "../video-upload-dialog";
import type { UploadProgress } from "@/entities/video";

const idleUploadState: UploadProgress = {
  status: "idle",
  progress: 0,
  uploadedBytes: 0,
  totalBytes: 0,
};

describe("VideoUploadDialog", () => {
  it("starts upload when a video file is pasted into the drop zone", async () => {
    const onUpload = vi.fn().mockResolvedValue(undefined);
    const file = new File(["video"], "lesson.mp4", { type: "video/mp4" });

    render(
      <VideoUploadDialog
        open
        onOpenChange={vi.fn()}
        uploadState={idleUploadState}
        onUpload={onUpload}
        onCancel={vi.fn()}
        onReset={vi.fn()}
      />,
    );

    fireEvent.paste(screen.getByText("Перетащите видео сюда"), {
      clipboardData: { files: [file] },
    });

    await waitFor(() => expect(onUpload).toHaveBeenCalledWith(file));
  });
});
