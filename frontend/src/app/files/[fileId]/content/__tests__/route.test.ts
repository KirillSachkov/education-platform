import { beforeEach, describe, expect, it, vi } from "vitest";

const authMock = vi.hoisted(() => vi.fn());
const fetchMock = vi.hoisted(() => vi.fn());

vi.mock("@/shared/auth/auth", () => ({
  auth: authMock,
}));

vi.stubGlobal("fetch", fetchMock);

import { isAllowedRedirect } from "../redirect-policy";
import { GET } from "../route";

beforeEach(() => {
  authMock.mockReset();
  fetchMock.mockReset();
});

describe("isAllowedRedirect", () => {
  it("allows localhost URLs", () => {
    expect(isAllowedRedirect("http://localhost/files/123")).toBe(true);
  });

  it("allows localhost with any port (dev S3/MinIO)", () => {
    expect(isAllowedRedirect("http://localhost:9000/files/123")).toBe(true);
    expect(isAllowedRedirect("http://localhost:8002/files/123")).toBe(true);
  });

  it("allows Yandex Cloud S3 URLs", () => {
    expect(isAllowedRedirect("https://bucket.storage.yandexcloud.net/file.png")).toBe(true);
  });

  it("allows Kinescope CDN URLs", () => {
    expect(isAllowedRedirect("https://cdn.kinescope.io/video.mp4")).toBe(true);
  });

  it("allows Kinescope CDN alt URLs", () => {
    expect(isAllowedRedirect("https://cdn.kinescopecdn.net/video.mp4")).toBe(true);
  });

  it("rejects unknown external URLs", () => {
    expect(isAllowedRedirect("https://evil.com/malware")).toBe(false);
  });

  it("rejects invalid URLs", () => {
    expect(isAllowedRedirect("not-a-url")).toBe(false);
  });

  it("rejects empty string", () => {
    expect(isAllowedRedirect("")).toBe(false);
  });

  it("rejects URLs from similar but different domains", () => {
    expect(isAllowedRedirect("https://fake-kinescope.io/video.mp4")).toBe(false);
    expect(isAllowedRedirect("https://notyandexcloud.net/file.png")).toBe(false);
  });
});

describe("GET", () => {
  it("forwards bearer token and disables shared caching for authenticated requests", async () => {
    authMock.mockResolvedValue({ accessToken: "test-access-token" });
    fetchMock.mockResolvedValue(
      new Response(null, {
        status: 302,
        headers: {
          Location: "https://cdn.kinescope.io/video.mp4",
        },
      }),
    );

    const response = await GET(new Request("http://localhost/files/123/content"), {
      params: Promise.resolve({ fileId: "123" }),
    });

    expect(fetchMock).toHaveBeenCalledWith(expect.stringMatching(/\/files\/123\/content\/$/), {
      redirect: "manual",
      cache: "no-store",
      headers: {
        Authorization: "Bearer test-access-token",
      },
    });
    expect(response.status).toBe(302);
    expect(response.headers.get("Location")).toBe("https://cdn.kinescope.io/video.mp4");
    expect(response.headers.get("Cache-Control")).toBe("private, no-store");
  });

  it("keeps anonymous requests unauthenticated and preserves public cache defaults", async () => {
    authMock.mockResolvedValue(null);
    fetchMock.mockResolvedValue(
      new Response("image-body", {
        status: 200,
        headers: {
          "Content-Type": "image/png",
        },
      }),
    );

    const response = await GET(new Request("http://localhost/files/456/content"), {
      params: Promise.resolve({ fileId: "456" }),
    });

    expect(fetchMock).toHaveBeenCalledWith(expect.stringMatching(/\/files\/456\/content\/$/), {
      redirect: "manual",
      cache: "no-store",
    });
    expect(response.status).toBe(200);
    expect(response.headers.get("Content-Type")).toBe("image/png");
    expect(response.headers.get("Cache-Control")).toBe("public, max-age=86400");
    expect(await response.text()).toBe("image-body");
  });

  it("forwards a bounded responsive-image width to FileService", async () => {
    authMock.mockResolvedValue(null);
    fetchMock.mockResolvedValue(new Response("image-body"));

    await GET(new Request("http://localhost/files/456/content?w=640"), {
      params: Promise.resolve({ fileId: "456" }),
    });

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringMatching(/\/files\/456\/content\/\?w=640$/),
      {
        redirect: "manual",
        cache: "no-store",
      },
    );
  });

  it.each(["invalid", "0", "-1", "1281"])(
    'does not forward invalid image width "%s"',
    async (width) => {
      authMock.mockResolvedValue(null);
      fetchMock.mockResolvedValue(new Response("image-body"));

      await GET(new Request(`http://localhost/files/456/content?w=${width}`), {
        params: Promise.resolve({ fileId: "456" }),
      });

      expect(fetchMock).toHaveBeenCalledWith(expect.stringMatching(/\/files\/456\/content\/$/), {
        redirect: "manual",
        cache: "no-store",
      });
    },
  );
});
