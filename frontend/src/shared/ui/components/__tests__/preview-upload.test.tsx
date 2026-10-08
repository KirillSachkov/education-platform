import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { PreviewUpload } from "../preview-upload";

vi.mock("@/shared/ui/components/image-cropper-dialog", () => ({
  ImageCropperDialog: ({
    imageFile,
    onCropped,
  }: {
    imageFile: File | null;
    onCropped: (file: File) => void;
  }) =>
    imageFile ? (
      <button type="button" onClick={() => onCropped(imageFile)}>
        crop {imageFile.name}
      </button>
    ) : null,
}));

function makeHook(upload = vi.fn(), overrides: Record<string, unknown> = {}) {
  return {
    upload,
    isUploading: false,
    previewUrl: null,
    uploadedAssetId: null,
    isDeleted: false,
    remove: vi.fn(),
    isRemoving: false,
    ...overrides,
  };
}

describe("PreviewUpload", () => {
  it("uploads an image pasted from the clipboard", () => {
    const upload = vi.fn();
    const file = new File(["image"], "cover.png", { type: "image/png" });

    render(<PreviewUpload hook={makeHook(upload)} alt="Обложка курса" />);

    fireEvent.paste(screen.getByText(/Перетащите изображение/), {
      clipboardData: { files: [file] },
    });
    fireEvent.click(screen.getByRole("button", { name: /crop cover.png/ }));

    expect(upload).toHaveBeenCalledWith(file);
  });

  it("replaces an existing image when a new file is dropped", () => {
    const upload = vi.fn();
    const file = new File(["image"], "replacement.webp", { type: "image/webp" });
    const { container } = render(
      <PreviewUpload
        hook={makeHook(upload)}
        imageId="existing"
        initialPreviewUrl="/files/existing/content"
        alt="Обложка курса"
      />,
    );

    fireEvent.drop(container.firstElementChild!, {
      dataTransfer: { files: [file] },
    });
    fireEvent.click(screen.getByRole("button", { name: /crop replacement.webp/ }));

    expect(upload).toHaveBeenCalledWith(file);
  });

  it("renders the existing cover as a responsive ContentImage (srcSet via ?w=)", () => {
    const { container } = render(
      <PreviewUpload
        hook={makeHook()}
        imageId="existing"
        initialPreviewUrl="/api/files/existing/content"
        alt="Обложка курса"
      />,
    );

    const img = container.querySelector("img")!;
    const srcSet = img.getAttribute("srcset") ?? img.getAttribute("srcSet");
    expect(srcSet).toContain("w=320 320w");
    expect(srcSet).toContain("w=1280 1280w");
  });

  it("renders a freshly-uploaded blob: preview without srcSet", () => {
    const { container } = render(
      <PreviewUpload
        hook={makeHook(vi.fn(), { previewUrl: "blob:http://localhost/fresh" })}
        alt="Обложка курса"
      />,
    );

    const img = container.querySelector("img")!;
    expect(img.getAttribute("src")).toBe("blob:http://localhost/fresh");
    expect(img.getAttribute("srcset") ?? img.getAttribute("srcSet")).toBeNull();
  });
});
