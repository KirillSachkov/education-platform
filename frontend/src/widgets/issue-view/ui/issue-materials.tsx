"use client";

import { useState } from "react";
import Link from "next/link";
import { ExternalLink, ArrowUpRight, Play, ChevronDown, ChevronUp } from "lucide-react";
import { ContentImage } from "@/shared/ui/components";
import { getCourseItemHref } from "@/entities/course";
import { useCourseSlug } from "@/shared/providers/course-id-provider";
import type { IssueDetailDto } from "@/entities/issue";

const COLLAPSED_LIMIT = 2;

interface IssueMaterialsProps {
  issueId: string;
  internalMaterials: IssueDetailDto["internalMaterials"];
  externalLinks: IssueDetailDto["externalLinks"];
}

type MaterialItem =
  | { kind: "internal"; data: IssueDetailDto["internalMaterials"][number] }
  | { kind: "external"; data: IssueDetailDto["externalLinks"][number] };

export function IssueMaterials({
  issueId,
  internalMaterials,
  externalLinks,
}: IssueMaterialsProps) {
  const courseSlug = useCourseSlug();
  const [expanded, setExpanded] = useState(false);

  const items: MaterialItem[] = [
    ...internalMaterials.map((data) => ({ kind: "internal", data }) as const),
    ...externalLinks.map((data) => ({ kind: "external", data }) as const),
  ];

  if (items.length === 0) {
    return null;
  }

  const hasMore = items.length > COLLAPSED_LIMIT;
  const visibleItems = expanded ? items : items.slice(0, COLLAPSED_LIMIT);
  const hiddenCount = items.length - COLLAPSED_LIMIT;

  return (
    <div className="mb-6">
      <h3 className="text-sm font-medium text-muted-foreground mb-3">Материалы</h3>
      <div className="space-y-2">
        {visibleItems.map((item) => {
          if (item.kind === "internal") {
            const material = item.data;
            const isLinkable = material.itemType === "Material";
            return (
              <Link
                key={`internal-${material.referenceId}`}
                href={
                  isLinkable
                    ? getCourseItemHref(courseSlug, material.itemType, material.referenceId, {
                        tab: "modules",
                        fromIssue: issueId,
                      })
                    : "#"
                }
                className="group flex items-center gap-3 rounded-xl border border-border hover:border-primary/30 hover:bg-accent/30 transition-all overflow-hidden"
              >
                {material.imageUrl ? (
                  <div className="relative w-24 aspect-video shrink-0 bg-muted">
                    <ContentImage
                      src={material.imageUrl}
                      alt=""
                      fill
                      loading="lazy"
                      className="object-cover"
                      sizes="96px"
                    />
                    <div className="absolute inset-0 flex items-center justify-center bg-black/20">
                      <div className="size-7 rounded-full bg-primary/80 flex items-center justify-center">
                        <Play
                          size={12}
                          className="text-primary-foreground ml-0.5"
                          fill="currentColor"
                        />
                      </div>
                    </div>
                  </div>
                ) : (
                  <div className="w-24 aspect-video shrink-0 bg-muted flex items-center justify-center">
                    <span className="w-3 h-3 rounded-full border-[1.5px] border-muted-foreground/40" />
                  </div>
                )}
                <div className="flex-1 min-w-0 py-3 pr-4">
                  <p className="text-sm font-medium truncate group-hover:text-primary transition-colors">
                    {material.title ?? material.itemType}
                  </p>
                  <p className="text-[11px] text-muted-foreground mt-0.5">
                    {material.itemType === "Material" ? "Материал" : material.itemType}
                    {material.isRequired && " · Обязательно"}
                  </p>
                </div>
                <ArrowUpRight
                  size={14}
                  strokeWidth={1.5}
                  className="text-muted-foreground group-hover:text-primary shrink-0 transition-colors mr-4"
                />
              </Link>
            );
          }

          const link = item.data;
          return (
            <a
              key={`external-${link.url}`}
              href={link.url}
              target="_blank"
              rel="noopener noreferrer"
              className="group flex items-center gap-3 px-4 py-3 rounded-xl border border-border hover:border-blue/30 hover:bg-accent/30 transition-all"
            >
              <ExternalLink size={16} strokeWidth={1.5} className="text-blue shrink-0" />
              <div className="flex-1 min-w-0">
                <p className="text-sm font-medium truncate group-hover:text-blue transition-colors">
                  {link.title}
                </p>
                {link.isRequired && <p className="text-xs text-muted-foreground">Обязательно</p>}
              </div>
              <ArrowUpRight
                size={14}
                strokeWidth={1.5}
                className="text-muted-foreground group-hover:text-blue shrink-0 transition-colors"
              />
            </a>
          );
        })}
      </div>

      {hasMore && (
        <button
          type="button"
          onClick={() => setExpanded((v) => !v)}
          className="mt-3 flex items-center gap-1.5 text-xs font-medium text-muted-foreground hover:text-foreground transition-colors"
        >
          {expanded ? (
            <>
              <ChevronUp size={14} />
              Свернуть
            </>
          ) : (
            <>
              <ChevronDown size={14} />
              Показать ещё {hiddenCount}
            </>
          )}
        </button>
      )}
    </div>
  );
}
