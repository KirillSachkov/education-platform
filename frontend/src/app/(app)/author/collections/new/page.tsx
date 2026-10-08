"use client";

import { useCreateCollection } from "@/features/collection-manage";
import { routes } from "@/shared/config/routes";
import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { Textarea } from "@/shared/ui/kit/textarea";
import { Loader2, Save } from "lucide-react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";

export default function AuthorCollectionCreatePage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const courseId = searchParams.get("courseId");
  const courseSlug = searchParams.get("courseSlug");
  const { createCollection, isPending } = useCreateCollection();
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");

  const backHref = courseSlug
    ? routes.authorCourseBuilder(courseSlug)
    : routes.authorCollections;
  const backLabel = courseId ? "\u2190 Назад к курсу" : "\u2190 Назад к подборкам";

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!title.trim()) return;

    const id = await createCollection({
      title: title.trim(),
      description: description.trim() || null,
      courseId: courseId || null,
    });
    router.push(
      routes.authorCollectionEdit(id, {
        courseId: courseId ?? undefined,
        courseSlug: courseSlug ?? undefined,
      }),
    );
  };

  return (
    <div className="min-h-full bg-background">
      {/* Header bar */}
      <div className="border-b border-border/70 px-4 py-4 md:px-6">
        <div className="mx-auto flex w-full max-w-5xl flex-col gap-3 md:flex-row md:items-center md:justify-between md:gap-4">
          <div className="min-w-0 space-y-1">
            <Link
              href={backHref}
              className="inline-flex text-sm text-muted-foreground transition-colors hover:text-foreground"
            >
              {backLabel}
            </Link>
            <h1 className="text-lg font-bold md:text-xl">Новая подборка</h1>
          </div>

          <div className="flex items-center gap-2 self-end shrink-0 md:self-auto">
            <Button variant="ghost" asChild>
              <Link href={backHref}>Отмена</Link>
            </Button>
            <Button
              type="submit"
              form="create-collection-form"
              disabled={isPending || !title.trim()}
            >
              {isPending ? (
                <Loader2 size={14} className="animate-spin" />
              ) : (
                <Save size={14} />
              )}
              {isPending ? "Создание..." : "Создать"}
            </Button>
          </div>
        </div>
      </div>

      {/* Form */}
      <form
        id="create-collection-form"
        onSubmit={(e) => void handleSubmit(e)}
        className="mx-auto max-w-5xl p-4 md:p-6 space-y-4"
      >
        <div className="space-y-2">
          <Label htmlFor="title">Название</Label>
          <Input
            id="title"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            placeholder="Название подборки"
            autoFocus
          />
        </div>
        <div className="space-y-2">
          <Label htmlFor="description">Описание</Label>
          <Textarea
            id="description"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Краткое описание (необязательно)"
            rows={3}
          />
        </div>
      </form>
    </div>
  );
}
