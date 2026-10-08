import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { auth } from "@/shared/auth/auth";
import { CourseBuilderView } from "./course-builder-view";

const API_URL =
  process.env.API_URL_INTERNAL ?? process.env.NEXT_PUBLIC_API_URL ?? "http://localhost/api";

interface Props {
  params: Promise<{ courseSlug: string }>;
}

async function resolveSlug(slug: string): Promise<string | null> {
  try {
    const res = await fetch(`${API_URL}/courses/by-slug/${slug}/`, {
      next: { revalidate: 300 },
    });
    if (!res.ok) return null;
    const data = await res.json();
    return data.result?.courseId ?? null;
  } catch {
    return null;
  }
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { courseSlug } = await params;
  const courseId = await resolveSlug(courseSlug);
  if (!courseId) return { title: "Редактирование курса" };

  try {
    // /courses/{id}/detail требует Courses.MANAGE — ходим под токеном автора.
    // `cache: "no-store"` обязателен: per-user данные нельзя сохранять в shared
    // fetch-кэше Next.js (он не учитывает Authorization при ключевании).
    const session = await auth();
    const accessToken = session?.accessToken;
    const res = await fetch(`${API_URL}/courses/${courseId}/detail/`, {
      headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined,
      cache: "no-store",
    });
    if (res.status === 404) notFound();
    if (!res.ok) return { title: "Редактирование курса" };
    const data = await res.json();
    const title = data.result?.title;
    return { title: title ? `Редактирование: ${title}` : "Редактирование курса" };
  } catch (error) {
    if (error && typeof error === "object" && "digest" in error) throw error;
    return { title: "Редактирование курса" };
  }
}

export default function CourseBuilderPage() {
  return <CourseBuilderView />;
}
