import { auth } from "@/shared/auth/auth";
import { FILE_API_URL, isAllowedRedirect } from "./redirect-policy";

const AUTHENTICATED_CACHE_CONTROL = "private, no-store";
const PUBLIC_CACHE_CONTROL = "public, max-age=86400";
const MAX_IMAGE_WIDTH = 1280;

function getRequestedImageWidth(request: Request): number | null {
  const rawWidth = new URL(request.url).searchParams.get("w");
  if (!rawWidth || !/^\d+$/.test(rawWidth)) {
    return null;
  }

  const width = Number(rawWidth);
  return Number.isSafeInteger(width) && width > 0 && width <= MAX_IMAGE_WIDTH ? width : null;
}

function getProxyHeaders(accessToken?: string): HeadersInit | undefined {
  if (!accessToken) {
    return undefined;
  }

  return {
    Authorization: `Bearer ${accessToken}`,
  };
}

function getCacheControl(accessToken?: string, response?: Response): string {
  if (accessToken) {
    return AUTHENTICATED_CACHE_CONTROL;
  }

  return response?.headers.get("Cache-Control") ?? PUBLIC_CACHE_CONTROL;
}

export async function GET(request: Request, { params }: { params: Promise<{ fileId: string }> }) {
  const { fileId } = await params;
  const session = await auth();
  const accessToken = session?.accessToken;
  const requestedWidth = getRequestedImageWidth(request);
  const widthQuery = requestedWidth === null ? "" : `?w=${String(requestedWidth)}`;
  const upstream = `${FILE_API_URL}/files/${encodeURIComponent(fileId)}/content/${widthQuery}`;
  const proxyHeaders = getProxyHeaders(accessToken);
  const res = await fetch(upstream, {
    redirect: "manual",
    cache: "no-store",
    ...(proxyHeaders ? { headers: proxyHeaders } : {}),
  });
  const cacheControl = getCacheControl(accessToken, res);

  if (res.status >= 300 && res.status < 400) {
    const location = res.headers.get("Location");
    if (location && isAllowedRedirect(location)) {
      return new Response(null, {
        status: res.status,
        headers: {
          Location: location,
          "Cache-Control": cacheControl,
        },
      });
    }
    if (location) {
      return new Response(null, { status: 502 });
    }
  }

  if (!res.ok) {
    return new Response(null, { status: res.status });
  }

  const responseHeaders = new Headers({
    "Content-Type": res.headers.get("Content-Type") ?? "application/octet-stream",
    "Cache-Control": cacheControl,
  });

  const contentDisposition = res.headers.get("Content-Disposition");
  if (contentDisposition) {
    responseHeaders.set("Content-Disposition", contentDisposition);
  }

  return new Response(res.body, {
    status: 200,
    headers: responseHeaders,
  });
}
