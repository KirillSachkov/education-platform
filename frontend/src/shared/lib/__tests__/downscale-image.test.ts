import { afterEach, describe, expect, it, vi } from "vitest";
import { downscaleImage } from "../downscale-image";

function imageFile(name = "photo.jpg", type = "image/jpeg", size = 4000): File {
  const file = new File(["x".repeat(size)], name, { type });
  return file;
}

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("downscaleImage", () => {
  it("passes through a non-image file untouched", async () => {
    const pdf = new File(["%PDF"], "doc.pdf", { type: "application/pdf" });
    const bitmapSpy = vi.fn();
    vi.stubGlobal("createImageBitmap", bitmapSpy);

    const result = await downscaleImage(pdf);

    expect(result).toBe(pdf);
    expect(bitmapSpy).not.toHaveBeenCalled();
  });

  it("passes through an image already within maxWidth", async () => {
    const file = imageFile();
    vi.stubGlobal(
      "createImageBitmap",
      vi.fn().mockResolvedValue({ width: 800, height: 600, close: vi.fn() }),
    );

    const result = await downscaleImage(file, { maxWidth: 1600 });

    expect(result).toBe(file);
  });

  it("returns the original file on any error", async () => {
    const file = imageFile();
    vi.stubGlobal("createImageBitmap", vi.fn().mockRejectedValue(new Error("decode failed")));

    const result = await downscaleImage(file);

    expect(result).toBe(file);
  });

  it("returns the original file when canvas has no 2d context", async () => {
    const file = imageFile();
    vi.stubGlobal(
      "createImageBitmap",
      vi.fn().mockResolvedValue({ width: 4000, height: 2000, close: vi.fn() }),
    );
    vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue(null);

    const result = await downscaleImage(file);

    expect(result).toBe(file);
  });

  it("downscales a large image to a WebP File, preserving aspect ratio", async () => {
    const file = imageFile("photo.jpg", "image/jpeg");
    const close = vi.fn();
    vi.stubGlobal(
      "createImageBitmap",
      vi.fn().mockResolvedValue({ width: 3200, height: 1600, close }),
    );

    const drawImage = vi.fn();
    const ctx = { drawImage } as unknown as CanvasRenderingContext2D;
    vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue(ctx);

    const blob = new Blob(["webp-bytes"], { type: "image/webp" });
    vi.spyOn(HTMLCanvasElement.prototype, "toBlob").mockImplementation((cb) => cb(blob));

    const result = await downscaleImage(file, { maxWidth: 1600, quality: 0.8 });

    expect(result).not.toBe(file);
    expect(result.type).toBe("image/webp");
    expect(result.name).toBe("photo.webp");
    expect(close).toHaveBeenCalled();
    // 3200 → 1600 is a 0.5 scale, so target dims are 1600×800 (aspect preserved).
    const [, , , w, h] = drawImage.mock.calls[0];
    expect(w).toBe(1600);
    expect(h).toBe(800);
  });

  it("returns the original file when toBlob yields null", async () => {
    const file = imageFile();
    vi.stubGlobal(
      "createImageBitmap",
      vi.fn().mockResolvedValue({ width: 4000, height: 2000, close: vi.fn() }),
    );
    vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue({
      drawImage: vi.fn(),
    } as unknown as CanvasRenderingContext2D);
    vi.spyOn(HTMLCanvasElement.prototype, "toBlob").mockImplementation((cb) => cb(null));

    const result = await downscaleImage(file);

    expect(result).toBe(file);
  });
});
