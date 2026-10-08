"use client";

import {
  roadmapsListQueryOptions,
  roadmapsApi,
  roadmapQueryKeys,
  type RoadmapSummaryDto,
} from "@/entities/roadmap";
import { coursesApi, coursesQueryOptions } from "@/entities/course";
import type { CourseSummaryDto } from "@/entities/course";
import { getErrorMessage } from "@/shared/api/errors";
import { formatRuPlural, RU_PLURALS } from "@/shared/lib/pluralize";
import { routes } from "@/shared/config/routes";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Card, CardDescription, CardHeader, CardTitle } from "@/shared/ui/kit/card";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/shared/ui/kit/alert-dialog";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { useMutation, useQuery, useQueryClient, queryOptions } from "@tanstack/react-query";
import { GraduationCap, Loader2, Map, Pencil, Plus, Trash2 } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { toast } from "sonner";

const STATUS_LABELS: Record<string, string> = {
  DRAFT: "Черновик",
  PUBLISHED: "Опубликован",
  ARCHIVED: "В архиве",
};

const STATUS_VARIANT: Record<string, "default" | "secondary" | "outline"> = {
  DRAFT: "secondary",
  PUBLISHED: "default",
  ARCHIVED: "outline",
};

export default function AuthorRoadmapsPage() {
  const queryClient = useQueryClient();
  const router = useRouter();
  const { data: roadmaps, isLoading } = useQuery(roadmapsListQueryOptions());
  const [createDialogOpen, setCreateDialogOpen] = useState(false);

  const deleteMutation = useMutation({
    mutationFn: (id: string) => roadmapsApi.deleteRoadmap(id),
    onSuccess: () => {
      toast.success("Роадмап удалён");
      queryClient.invalidateQueries({ queryKey: [roadmapQueryKeys.base, "list"] });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка удаления роадмапа")),
  });

  if (isLoading) {
    return (
      <div className="flex h-64 items-center justify-center">
        <Loader2 className="size-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  return (
    <div className="space-y-6 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Роадмапы</h1>
        <Button onClick={() => setCreateDialogOpen(true)} className="gap-2">
          <Plus className="size-4" />
          Создать
        </Button>
      </div>

      {(!roadmaps || roadmaps.length === 0) && (
        <div className="flex h-48 flex-col items-center justify-center gap-3 text-muted-foreground">
          <Map className="size-10" />
          <p className="text-sm">Пока нет роадмапов</p>
        </div>
      )}

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {roadmaps?.map((roadmap: RoadmapSummaryDto) => (
          <Card key={roadmap.id} className="group relative">
            <Link href={routes.authorRoadmapEditor(roadmap.id)}>
              <CardHeader className="pb-3">
                <div className="flex items-start justify-between gap-2 pr-20 sm:pr-0">
                  <CardTitle className="text-base">{roadmap.title}</CardTitle>
                  <Badge
                    variant={STATUS_VARIANT[roadmap.status] ?? "secondary"}
                    className="shrink-0"
                  >
                    {STATUS_LABELS[roadmap.status] ?? roadmap.status}
                  </Badge>
                </div>
                {roadmap.description && (
                  <CardDescription className="line-clamp-2">{roadmap.description}</CardDescription>
                )}
                <div className="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
                  <span>{formatRuPlural(roadmap.nodeCount, RU_PLURALS.element)}</span>
                  {roadmap.courseId && <span>Привязан к курсу</span>}
                  {roadmap.slug && <span className="truncate">/{roadmap.slug}</span>}
                </div>
              </CardHeader>
            </Link>

            <div className="absolute right-2 top-2 flex gap-0.5 opacity-100 transition-opacity sm:opacity-0 sm:group-hover:opacity-100">
              <Button variant="ghost" size="icon" className="size-9 sm:size-7" asChild>
                <Link href={routes.authorRoadmapEditor(roadmap.id)}>
                  <Pencil className="size-3.5" />
                </Link>
              </Button>
              <AlertDialog>
                <AlertDialogTrigger asChild>
                  <Button variant="ghost" size="icon" className="size-9 text-destructive sm:size-7">
                    <Trash2 className="size-3.5" />
                  </Button>
                </AlertDialogTrigger>
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>Удалить роадмап?</AlertDialogTitle>
                    <AlertDialogDescription>
                      Роадмап «{roadmap.title}» будет безвозвратно удалён. Это действие нельзя
                      отменить.
                    </AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>Отмена</AlertDialogCancel>
                    <AlertDialogAction
                      className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
                      onClick={(e) => {
                        e.preventDefault();
                        deleteMutation.mutate(roadmap.id);
                      }}
                    >
                      Удалить
                    </AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
            </div>
          </Card>
        ))}
      </div>

      <CreateRoadmapDialog
        open={createDialogOpen}
        onOpenChange={setCreateDialogOpen}
        existingCourseIds={
          new Set((roadmaps ?? []).filter((r) => r.courseId).map((r) => r.courseId!))
        }
        onCreated={(id) => router.push(routes.authorRoadmapEditor(id))}
      />
    </div>
  );
}

function CreateRoadmapDialog({
  open,
  onOpenChange,
  existingCourseIds,
  onCreated,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  existingCourseIds: Set<string>;
  onCreated: (roadmapId: string) => void;
}) {
  const [title, setTitle] = useState("Новый роадмап");
  const [slug, setSlug] = useState("");
  const [selectedCourseId, setSelectedCourseId] = useState<string | null>(null);

  function handleDialogChange(open: boolean) {
    onOpenChange(open);
    if (!open) {
      setTitle("Новый роадмап");
      setSlug("");
      setSelectedCourseId(null);
    }
  }

  const { data: courses } = useQuery(
    queryOptions({
      queryKey: [coursesQueryOptions.baseKey, "my", "roadmap-create"],
      queryFn: ({ signal }) => coursesApi.getMyCourses({ limit: 50 }, { signal }),
      select: (data) => data.result?.items ?? [],
      enabled: open,
    }),
  );

  const availableCourses = (courses ?? []).filter(
    (c: CourseSummaryDto) => !existingCourseIds.has(c.id),
  );

  const createMutation = useMutation({
    mutationFn: () =>
      roadmapsApi.createRoadmap({
        title,
        courseId: selectedCourseId,
        slug: slug || undefined,
      }),
    onSuccess: (data) => {
      toast.success("Роадмап создан");
      onOpenChange(false);
      onCreated(data.result!);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка создания роадмапа")),
  });

  return (
    <Dialog open={open} onOpenChange={handleDialogChange}>
      <DialogContent className="max-w-sm">
        <DialogHeader>
          <DialogTitle>Создать роадмап</DialogTitle>
          <DialogDescription>Укажите название и выберите курс (опционально)</DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div>
            <Label className="text-xs">Название</Label>
            <Input
              className="mt-1"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="Название роадмапа"
            />
          </div>

          <div>
            <Label className="text-xs">URL-слаг (опционально)</Label>
            <Input
              className="mt-1"
              value={slug}
              onChange={(e) => setSlug(e.target.value)}
              placeholder="my-roadmap"
            />
            <p className="mt-1 text-2xs text-muted-foreground">
              Только строчные латинские буквы, цифры и дефисы
            </p>
          </div>

          <div>
            <Label className="text-xs">Привязка к курсу (опционально)</Label>
            <div className="mt-1.5 space-y-1">
              <button
                type="button"
                onClick={() => setSelectedCourseId(null)}
                className={`flex w-full items-center gap-2 rounded-md border px-3 py-2 text-left text-sm transition-colors ${
                  selectedCourseId === null
                    ? "border-primary bg-primary/5"
                    : "border-border hover:bg-accent"
                }`}
              >
                <Map className="size-4 text-muted-foreground" />
                <span>Standalone (без курса)</span>
              </button>

              {availableCourses.map((course: CourseSummaryDto) => (
                <button
                  key={course.id}
                  type="button"
                  onClick={() => setSelectedCourseId(course.id)}
                  className={`flex w-full items-center gap-2 rounded-md border px-3 py-2 text-left text-sm transition-colors ${
                    selectedCourseId === course.id
                      ? "border-primary bg-primary/5"
                      : "border-border hover:bg-accent"
                  }`}
                >
                  <GraduationCap className="size-4 text-purple-500" />
                  <span className="truncate">{course.title}</span>
                </button>
              ))}
            </div>
          </div>

          <Button
            className="w-full"
            disabled={!title.trim() || createMutation.isPending}
            onClick={() => createMutation.mutate()}
          >
            {createMutation.isPending ? <Loader2 className="mr-2 size-4 animate-spin" /> : null}
            Создать
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
