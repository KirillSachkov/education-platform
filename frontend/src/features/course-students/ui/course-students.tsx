"use client";

import { type CourseBuilderDto } from "@/entities/course";
import {
  courseStudentsInfiniteQueryOptions,
  type CourseStudentDto,
} from "@/entities/course-student";
import { getErrorMessage } from "@/shared/api";
import { useInfiniteScroll } from "@/shared/hooks";
import { UserAvatar } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { routes } from "@/shared/config/routes";
import Link from "next/link";
import { StudentProgressPanel } from "./student-progress-panel";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { Input } from "@/shared/ui/kit/input";
import { useInfiniteQuery } from "@tanstack/react-query";
import { Loader2, Users } from "lucide-react";
import type { ChangeEvent } from "react";
import { useState } from "react";
import { useDebouncedCallback } from "use-debounce";

interface CourseStudentsProps {
  courseId: string;
  course: CourseBuilderDto;
}

export function CourseStudents({ courseId, course }: CourseStudentsProps) {
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [pageSize] = useState(20);
  const [selectedStudent, setSelectedStudent] = useState<CourseStudentDto | null>(null);

  const debouncedSetSearch = useDebouncedCallback((value: string) => {
    setSearch(value);
  }, 300);

  const studentsQuery = useInfiniteQuery(
    courseStudentsInfiniteQueryOptions(courseId, {
      pageSize,
      search: search || undefined,
    }),
  );

  const setCursorRef = useInfiniteScroll({
    hasNextPage: studentsQuery.hasNextPage,
    isFetchingNextPage: studentsQuery.isFetchingNextPage,
    fetchNextPage: () => {
      void studentsQuery.fetchNextPage();
    },
  });

  const students = studentsQuery.data?.items ?? [];
  const totalCount = studentsQuery.data?.totalCount ?? 0;
  const enrolledInfo = `${totalCount} ${totalCount === 1 ? "участник" : totalCount < 5 ? "участника" : "участников"}`;

  const onSearchChange = (event: ChangeEvent<HTMLInputElement>) => {
    const value = event.target.value;
    setSearchInput(value);
    debouncedSetSearch(value.trim());
  };

  const getParticipantRole = (studentUserId: string) => {
    return studentUserId === course.authorId ? "Автор курса" : "Ученик";
  };

  return (
    <div className="space-y-4">
      <Card className="p-5 gap-0">
        <div className="flex items-start justify-between gap-3 mb-4">
          <div>
            <h2 className="text-base font-semibold flex items-center gap-2">
              <Users size={16} />
              Участники курса
            </h2>
            <p className="text-sm text-muted-foreground">{enrolledInfo}</p>
          </div>
        </div>

        <div className="mb-4">
          <Input
            value={searchInput}
            onChange={onSearchChange}
            placeholder="Поиск: имя, username, Telegram"
          />
        </div>

        {studentsQuery.isLoading ? (
          <div className="flex items-center justify-center py-8 text-muted-foreground">
            <Loader2 className="size-4 animate-spin mr-2" />
            Загрузка участников...
          </div>
        ) : studentsQuery.isError ? (
          <div className="py-8 text-center text-sm text-destructive space-y-2">
            <p>{getErrorMessage(studentsQuery.error, "Не удалось загрузить участников курса")}</p>
            <Button type="button" variant="outline" onClick={() => studentsQuery.refetch()}>
              Повторить
            </Button>
          </div>
        ) : students.length === 0 ? (
          <div className="py-8 text-center text-sm text-muted-foreground">Участники не найдены</div>
        ) : (
          <div className="flex flex-col divide-y rounded-lg border mb-3 max-h-[60vh] w-full overflow-x-hidden overflow-y-auto">
            {students.map((student: CourseStudentDto) => (
              <button
                key={student.enrollmentId}
                type="button"
                onClick={() => setSelectedStudent(student)}
                className="p-3 flex items-center justify-between gap-3 w-full min-w-0 text-left transition-colors hover:bg-muted/50 focus-visible:bg-muted/50 focus-visible:outline-none cursor-pointer"
              >
                <UserAvatar
                  name={student.name ?? student.username}
                  avatarId={student.avatarId}
                  userId={student.userId}
                  className="size-9 shrink-0"
                />
                <div className="min-w-0 flex-1">
                  <span className="text-sm font-medium truncate block">
                    {student.name?.trim() || student.username || "Без имени"}
                  </span>
                  <p className="text-xs text-muted-foreground truncate">
                    {student.email ?? "Email недоступен"}
                  </p>
                </div>
                <div className="text-right shrink-0">
                  <Badge
                    variant={student.userId === course.authorId ? "default" : "secondary"}
                    className="mb-1"
                  >
                    {getParticipantRole(student.userId)}
                  </Badge>
                  <p className="text-xs text-muted-foreground">
                    {new Date(student.enrolledAt).toLocaleDateString("ru-RU")}
                  </p>
                </div>
                <Icons.chevronRight size={16} className="text-muted-foreground/60 shrink-0" />
              </button>
            ))}
            {/* Сентинел ОБЯЗАН жить внутри overflow-y-auto контейнера, иначе он всегда
                во вьюпорте и IntersectionObserver сливает все страницы при открытии. */}
            <div ref={setCursorRef} className="h-1" />
            {studentsQuery.isFetchingNextPage && (
              <div className="flex justify-center py-2">
                <Loader2 className="size-4 animate-spin text-muted-foreground" />
              </div>
            )}
          </div>
        )}
      </Card>

      <Card className="p-5 gap-0 bg-muted/30">
        <h3 className="text-sm font-semibold mb-1">Как добавить участников</h3>
        <p className="text-sm text-muted-foreground mb-4">
          Список участников теперь формируется автоматически из выданных доступов. Чтобы открыть
          курс новому ученику, выдайте ему план, который покрывает этот курс — на странице плана во
          вкладке «Кому выдан доступ».
        </p>
        <Button asChild variant="outline" className="self-start">
          <Link href={routes.authorPlans}>Управление доступом в планах</Link>
        </Button>
      </Card>

      <StudentProgressPanel
        courseId={courseId}
        course={course}
        userId={selectedStudent?.userId ?? null}
        studentName={selectedStudent?.name?.trim() || selectedStudent?.username || "Без имени"}
        avatarId={selectedStudent?.avatarId ?? null}
        open={!!selectedStudent}
        onOpenChange={(next) => {
          if (!next) setSelectedStudent(null);
        }}
      />
    </div>
  );
}
