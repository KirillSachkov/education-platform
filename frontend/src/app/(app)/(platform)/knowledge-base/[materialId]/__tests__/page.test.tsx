import { renderToStaticMarkup } from "react-dom/server";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { MaterialDetailDto } from "@/entities/material";

const mocks = vi.hoisted(() => ({
  fetchAnonymous: vi.fn<(path: string, init?: RequestInit) => Promise<unknown>>(),
  materialView: vi.fn<(props: { initialMaterial: MaterialDetailDto | null | undefined }) => void>(),
  articleJsonLd: vi.fn<(props: { headline: string; description: string }) => void>(),
}));

vi.mock("@/widgets/material-view", () => ({
  MaterialView: ({ initialMaterial }: { initialMaterial?: MaterialDetailDto | null }) => {
    mocks.materialView({ initialMaterial });
    return (
      <main>
        {initialMaterial ? <h1>{initialMaterial.title}</h1> : <p>locked preview</p>}
        {initialMaterial?.description ? <p>{initialMaterial.description}</p> : null}
        {initialMaterial?.content ? <article>{initialMaterial.content}</article> : null}
      </main>
    );
  },
}));

vi.mock("@/shared/seo", () => ({
  ArticleJsonLd: (props: { headline: string; description: string }) => {
    mocks.articleJsonLd(props);
    return <script data-headline={props.headline} data-description={props.description} />;
  },
  BreadcrumbJsonLd: () => null,
  buildEntityMetadata: vi.fn((input: unknown) => input),
  fetchAnonymous: (path: string, init?: RequestInit) => mocks.fetchAnonymous(path, init),
  stripMarkdown: (value: string) => value.replace(/[#*_]/g, "").trim(),
  truncate: (value: string, max: number) => value.slice(0, max),
}));

import MaterialPage, { generateMetadata } from "../page";

const publicPreview = {
  id: "material-id",
  title: "Публичная статья",
  kind: "ARTICLE",
  accessType: "PUBLIC",
  imageUrl: null,
  publishedAt: "2026-07-01T00:00:00Z",
  updatedAt: "2026-07-14T00:00:00Z",
};

const publicDetail = {
  id: "material-id",
  authorId: "author-id",
  title: publicPreview.title,
  description: "**Безопасное уникальное описание**",
  content: "# Полное публичное тело материала",
  kind: "ARTICLE",
  status: "PUBLISHED",
  accessType: "PUBLIC",
  isAccessible: true,
  imageId: null,
  imageUrl: null,
  videoId: null,
  video: null,
  createdAt: "2026-07-01T00:00:00Z",
  updatedAt: "2026-07-14T00:00:00Z",
  courseCount: 0,
  chapters: [],
} satisfies MaterialDetailDto;

describe("knowledge material SSR access hygiene", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("fetches and server-renders full detail only for PUBLIC material", async () => {
    mocks.fetchAnonymous.mockImplementation((path) => {
      if (path.endsWith("/preview/")) return Promise.resolve(publicPreview);
      if (path.endsWith("/detail/")) return Promise.resolve(publicDetail);
      return Promise.resolve(null);
    });

    const html = renderToStaticMarkup(
      await MaterialPage({ params: Promise.resolve({ materialId: "material-id" }) }),
    );

    expect(mocks.fetchAnonymous).toHaveBeenCalledWith("/materials/material-id/preview/", undefined);
    expect(mocks.fetchAnonymous).toHaveBeenCalledWith("/materials/material-id/detail/", {
      cache: "no-store",
    });
    expect(html).toContain("<h1>Публичная статья</h1>");
    expect(html).toContain("Полное публичное тело материала");
    expect(mocks.materialView.mock.calls.at(-1)?.[0].initialMaterial).toEqual(publicDetail);
    const jsonLdProps = mocks.articleJsonLd.mock.calls.at(-1)?.[0];
    expect(jsonLdProps?.headline).toBe(publicPreview.title);
    expect(jsonLdProps?.description).toBe("Безопасное уникальное описание");
  });

  it.each(["REGISTERED", "ENROLLED"] as const)(
    "never fetches or passes the %s body",
    async (accessType) => {
      mocks.fetchAnonymous.mockImplementation((path) => {
        if (path.endsWith("/preview/")) {
          return Promise.resolve({ ...publicPreview, accessType });
        }
        return Promise.reject(new Error(`restricted detail fetch: ${path}`));
      });

      const html = renderToStaticMarkup(
        await MaterialPage({ params: Promise.resolve({ materialId: "material-id" }) }),
      );

      expect(mocks.fetchAnonymous).toHaveBeenCalledTimes(1);
      expect(mocks.fetchAnonymous).not.toHaveBeenCalledWith("/materials/material-id/detail/");
      expect(mocks.materialView.mock.calls.at(-1)?.[0].initialMaterial).toBeUndefined();
      expect(html).not.toContain("Полное публичное тело материала");
      const restrictedJsonLdProps = mocks.articleJsonLd.mock.calls.at(-1)?.[0];
      expect(restrictedJsonLdProps?.description).toContain(publicPreview.title);
    },
  );

  it("builds public metadata from safe description content", async () => {
    mocks.fetchAnonymous.mockImplementation((path) =>
      Promise.resolve(path.endsWith("/preview/") ? publicPreview : publicDetail),
    );

    const metadata = await generateMetadata({
      params: Promise.resolve({ materialId: "material-id" }),
    });

    expect(metadata).toEqual(
      expect.objectContaining({ description: "Безопасное уникальное описание" }),
    );
  });
});
