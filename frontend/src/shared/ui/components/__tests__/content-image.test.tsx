import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { API_ORIGIN } from "@/shared/api";
import { ContentImage } from "../content-image";

describe("ContentImage", () => {
  it("renders a width srcSet for a content URL", () => {
    const { container } = render(
      <ContentImage src="/api/files/abc/content" alt="cover" sizes="50vw" />,
    );
    const img = container.querySelector("img")!;
    expect(img.getAttribute("src")).toBe(`${API_ORIGIN}/api/files/abc/content`);
    const srcSet = img.getAttribute("srcset") ?? img.getAttribute("srcSet");
    expect(srcSet).toContain("w=320 320w");
    expect(srcSet).toContain("w=1280 1280w");
    expect(img.getAttribute("sizes")).toBe("50vw");
  });

  it("omits srcSet and sizes for an external URL", () => {
    const { container } = render(
      <ContentImage src="https://kinescope.io/poster.jpg" alt="poster" sizes="50vw" />,
    );
    const img = container.querySelector("img")!;
    expect(img.getAttribute("src")).toBe("https://kinescope.io/poster.jpg");
    expect(img.getAttribute("srcset") ?? img.getAttribute("srcSet")).toBeNull();
    // sizes is dropped when there's no srcSet (would otherwise be a no-op).
    expect(img.getAttribute("sizes")).toBeNull();
  });

  it("defaults loading=lazy and decoding=async", () => {
    const { container } = render(
      <ContentImage src="/api/files/abc/content" alt="cover" sizes="50vw" />,
    );
    const img = container.querySelector("img")!;
    expect(img.getAttribute("loading")).toBe("lazy");
    expect(img.getAttribute("decoding")).toBe("async");
  });

  it("applies fill positioning classes when fill is set, merging className", () => {
    const { container } = render(
      <ContentImage
        src="/api/files/abc/content"
        alt="cover"
        sizes="50vw"
        fill
        className="object-cover rounded-lg"
      />,
    );
    const img = container.querySelector("img")!;
    expect(img.className).toContain("absolute");
    expect(img.className).toContain("inset-0");
    expect(img.className).toContain("size-full");
    expect(img.className).toContain("object-cover");
    expect(img.className).toContain("rounded-lg");
  });

  it("does not add fill classes by default", () => {
    const { container } = render(
      <ContentImage src="/api/files/abc/content" alt="cover" sizes="50vw" className="x" />,
    );
    const img = container.querySelector("img")!;
    expect(img.className).toBe("x");
  });
});
