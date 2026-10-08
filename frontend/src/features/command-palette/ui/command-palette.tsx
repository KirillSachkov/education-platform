"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { ENTITY_ICONS } from "@/shared/config/entity-icons";
import { User, LogOut } from "lucide-react";
import {
  CommandDialog,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
  CommandShortcut,
} from "@/shared/ui/kit/command";
import { routes } from "@/shared/config/routes";
import { fullLogout } from "@/shared/auth";
import { useCourseContext } from "@/shared/providers/course-id-provider";
import {
  courseCurriculumQueryOptions,
  getCourseItemHref,
} from "@/entities/course";

interface CommandPaletteProps {
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
}

export function CommandPalette({
  open: controlledOpen,
  onOpenChange,
}: CommandPaletteProps) {
  const [internalOpen, setInternalOpen] = useState(false);
  const router = useRouter();
  const courseCtx = useCourseContext();

  const { data: curriculum } = useQuery({
    ...courseCurriculumQueryOptions(courseCtx?.courseId ?? ""),
    enabled: !!courseCtx?.courseId,
  });

  const courseItems = (() => {
    if (!courseCtx || !curriculum) return [];
    const seen = new Set<string>();
    return curriculum.sections.flatMap((section) =>
      section.items
        .filter((item) => {
          if (seen.has(item.id)) return false;
          seen.add(item.id);
          return true;
        })
        .map((item) => ({
          ...item,
          sectionTitle: section.title,
          href: getCourseItemHref(courseCtx.courseSlug, item.itemType, item.id),
        })),
    );
  })();

  const isOpen = controlledOpen ?? internalOpen;

  function setOpen(value: boolean) {
    setInternalOpen(value);
    onOpenChange?.(value);
  }

  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "k" && (e.metaKey || e.ctrlKey)) {
        e.preventDefault();
        setInternalOpen((prev) => {
          const next = !(controlledOpen ?? prev);
          onOpenChange?.(next);
          return next;
        });
      }
    }

    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [controlledOpen, onOpenChange]);

  function runAction(fn: () => void) {
    setOpen(false);
    fn();
  }

  return (
    <CommandDialog
      open={isOpen}
      onOpenChange={setOpen}
      title="Командная палитра"
      description="Поиск по навигации и действиям"
    >
      <CommandInput placeholder="Поиск..." />
      <CommandList>
        <CommandEmpty>Ничего не найдено</CommandEmpty>
        {courseItems.length > 0 && (
          <CommandGroup heading="Текущий курс">
            {courseItems.map((item) => (
              <CommandItem
                key={item.id}
                onSelect={() => runAction(() => router.push(item.href))}
              >
                {item.itemType === "Material" ? (
                  <ENTITY_ICONS.lesson className="mr-2 size-4" />
                ) : (
                  <ENTITY_ICONS.issue className="mr-2 size-4" />
                )}
                <div className="flex flex-col">
                  <span>{item.title}</span>
                  <span className="text-xs text-muted-foreground">
                    {item.sectionTitle}
                  </span>
                </div>
              </CommandItem>
            ))}
          </CommandGroup>
        )}
        <CommandGroup heading="Навигация">
          <CommandItem onSelect={() => runAction(() => router.push("/"))}>
            <ENTITY_ICONS.module className="mr-2 size-4" />
            Пространства
            <CommandShortcut>Навигация</CommandShortcut>
          </CommandItem>
          <CommandItem onSelect={() => runAction(() => router.push(routes.profile))}>
            <User className="mr-2 size-4" />
            Профиль
          </CommandItem>
        </CommandGroup>
        <CommandGroup heading="Действия">
          <CommandItem onSelect={() => runAction(() => fullLogout())}>
            <LogOut className="mr-2 size-4" />
            Выйти
          </CommandItem>
        </CommandGroup>
      </CommandList>
    </CommandDialog>
  );
}
