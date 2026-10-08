import { IssuePageClient } from "./page-client";

// Anon/not-enrolled users are allowed through — the client view shows a lock-preview
// with enroll/login CTA instead of a hard redirect to /login.
export default async function IssuePage({
  params,
}: {
  params: Promise<{ courseSlug: string; issueId: string }>;
}) {
  const { issueId } = await params;
  return <IssuePageClient issueId={issueId} />;
}
