import { fireEvent, render } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { MaterialPreviewUpload } from "../material-preview-upload";

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

describe("MaterialPreviewUpload", () => {
  it("replaces an existing preview when an image is pasted", () => {
    const upload = vi.fn();
    const file = new File(["image"], "material-preview.jpg", {
      type: "image/jpeg",
    });
    const { container } = render(
      <MaterialPreviewUpload
        hook={makeHook(upload)}
        imageId="existing"
        initialPreviewUrl="/files/existing/content"
      />,
    );

    fireEvent.paste(container.firstElementChild!, {
      clipboardData: { files: [file] },
    });

    expect(upload).toHaveBeenCalledWith(file);
  });

  it("renders the existing cover as a responsive ContentImage (srcSet via ?w=)", () => {
    const { container } = render(
      <MaterialPreviewUpload
        hook={makeHook()}
        imageId="existing"
        initialPreviewUrl="/api/files/existing/content"
      />,
    );

    const img = container.querySelector("img")!;
    const srcSet = img.getAttribute("srcset") ?? img.getAttribute("srcSet");
    expect(srcSet).toContain("w=320 320w");
    expect(srcSet).toContain("w=1280 1280w");
  });

  it("renders a freshly-uploaded blob: preview without srcSet (backend can't resize a local blob)", () => {
    const { container } = render(
      <MaterialPreviewUpload
        hook={makeHook(vi.fn(), { previewUrl: "blob:http://localhost/fresh" })}
      />,
    );

    const img = container.querySelector("img")!;
    expect(img.getAttribute("src")).toBe("blob:http://localhost/fresh");
    expect(img.getAttribute("srcset") ?? img.getAttribute("srcSet")).toBeNull();
  });
});
