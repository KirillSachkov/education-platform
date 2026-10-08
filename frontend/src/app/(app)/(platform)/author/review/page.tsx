import type { Metadata } from "next";
import { SubmissionReviewPage } from "@/features/submission-review";
import { auth } from "@/shared/auth/auth";
import { ROLES } from "@/shared/auth/roles";
import { redirect } from "next/navigation";

export const metadata: Metadata = {
  title: "Проверка работ",
};

const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

export default async function AuthorReviewPage({
  searchParams,
}: {
  searchParams: Promise<{ submissionId?: string }>;
}) {
  const session = await auth();
  const roles = Array.isArray(session?.user.roles) ? session.user.roles : [];
  const hasAccess =
    roles.includes(ROLES.MODERATOR) || roles.includes(ROLES.ADMIN) || roles.includes(ROLES.OWNER);

  if (!hasAccess) {
    redirect("/");
  }

  const { submissionId } = await searchParams;
  const focusedSubmissionId =
    submissionId !== undefined && UUID_PATTERN.test(submissionId) ? submissionId : undefined;

  return focusedSubmissionId === undefined ? (
    <SubmissionReviewPage />
  ) : (
    <SubmissionReviewPage focusedSubmissionId={focusedSubmissionId} />
  );
}
