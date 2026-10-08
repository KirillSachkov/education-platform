"use client";

import Link from "next/link";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/shared/ui/kit/breadcrumb";
import { Fragment } from "react";

interface BreadcrumbSegment {
  label: string;
  href?: string;
}

interface CourseBreadcrumbProps {
  items: BreadcrumbSegment[];
  /**
   * На <md полный trail заменяется одной компактной «назад»-ссылкой на
   * ближайшего родителя (#511): на 390px крошки сжимались в бесполезные
   * огрызки «База з… › Чек-лист под…». Заголовок текущей страницы при этом
   * читается в h1 ниже. ≥md — полный trail как раньше.
   */
  compactOnMobile?: boolean;
}

export function CourseBreadcrumb({ items, compactOnMobile = false }: CourseBreadcrumbProps) {
  // Ближайший родитель с href (последний элемент — текущая страница, без ссылки).
  const mobileParent = compactOnMobile
    ? [...items].reverse().find((item) => item.href)
    : undefined;

  return (
    <>
      {mobileParent?.href && (
        <Link
          href={mobileParent.href}
          className="inline-flex max-w-full items-center gap-1 text-xs text-muted-foreground transition-colors hover:text-foreground md:hidden"
        >
          <Icons.chevronLeft className="size-3.5 shrink-0" />
          <span className="truncate">{mobileParent.label}</span>
        </Link>
      )}
      <Breadcrumb className={cn("min-w-0", mobileParent?.href && "hidden md:block")}>
        <BreadcrumbList className="flex-nowrap text-xs sm:text-sm">
          {items.map((item, index) => {
            const isLast = index === items.length - 1;
            return (
              <Fragment key={index}>
                {index > 0 && <BreadcrumbSeparator />}
                <BreadcrumbItem className="min-w-0">
                  {item.href && !isLast ? (
                    <BreadcrumbLink asChild>
                      <Link
                        href={item.href}
                        className="block max-w-[96px] truncate whitespace-nowrap sm:max-w-[160px] md:max-w-[220px]"
                      >
                        {item.label}
                      </Link>
                    </BreadcrumbLink>
                  ) : (
                    <BreadcrumbPage className="block max-w-[140px] truncate whitespace-nowrap sm:max-w-[220px] md:max-w-[300px]">
                      {item.label}
                    </BreadcrumbPage>
                  )}
                </BreadcrumbItem>
              </Fragment>
            );
          })}
        </BreadcrumbList>
      </Breadcrumb>
    </>
  );
}
