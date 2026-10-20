import { handlers, signIn } from "@/shared/auth/auth";
import { resolveWorkspaceCallback } from "@/shared/lib/workspace-callback";
import { type NextRequest, NextResponse } from "next/server";

type RouteContext = {
  params: Promise<{
    nextauth?: string[];
  }>;
};

export async function GET(request: NextRequest, context: RouteContext) {
  const params = await context.params;
  const segments = params.nextauth ?? [];

  // Auth.js v5 does not support GET /signin/:provider.
  // Some clients may still hit this URL, so we normalize it to a server-side signIn.
  if (segments[0] === "signin" && segments[1]) {
    const callbackUrl = resolveWorkspaceCallback(
      new URL(request.url).searchParams.get("callbackUrl"),
    );

    const redirectUrl = (await signIn(segments[1], {
      redirect: false,
      redirectTo: callbackUrl,
    })) as string | undefined;

    if (!redirectUrl) {
      return NextResponse.redirect(new URL("/api/auth/error?error=Configuration", request.url));
    }

    const target = redirectUrl.startsWith("http")
      ? redirectUrl
      : new URL(redirectUrl, request.url).toString();

    return NextResponse.redirect(target);
  }

  return handlers.GET(request);
}

export const POST = handlers.POST;
