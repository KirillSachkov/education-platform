"use client";

import { cn } from "@/shared/lib/css";
import { getFileExtension } from "@/shared/lib/markdown-assets";
import { Icons, type IconComponent } from "@/shared/ui/icons";

const EXTENSION_ICON: Record<string, IconComponent> = {
  pdf: Icons.document,
  doc: Icons.document,
  docx: Icons.document,
  txt: Icons.document,
  md: Icons.document,
  markdown: Icons.document,
  rtf: Icons.document,
  csv: Icons.fileSheet,
  xls: Icons.fileSheet,
  xlsx: Icons.fileSheet,
  ppt: Icons.presentation,
  pptx: Icons.presentation,
  zip: Icons.fileArchive,
  tar: Icons.fileArchive,
  gz: Icons.fileArchive,
  rar: Icons.fileArchive,
  "7z": Icons.fileArchive,
  json: Icons.fileCode,
  excalidraw: Icons.fileCode,
  svg: Icons.fileImage,
  png: Icons.fileImage,
  jpg: Icons.fileImage,
  jpeg: Icons.fileImage,
  webp: Icons.fileImage,
  gif: Icons.fileImage,
};

interface FileAttachmentCardProps {
  href: string;
  label: string;
  className?: string;
}

export function FileAttachmentCard({ href, label, className }: FileAttachmentCardProps) {
  const ext = extractExtensionFromLabel(label);
  const Icon = EXTENSION_ICON[ext] ?? Icons.document;

  return (
    <a
      href={href}
      target="_blank"
      rel="noopener noreferrer"
      download
      className={cn(
        "group inline-flex max-w-full items-center gap-3 my-2 rounded-lg border bg-card px-3 py-2 no-underline transition-colors",
        "hover:border-primary/50 hover:bg-accent/40",
        className,
      )}
    >
      <span
        className={cn(
          "flex h-9 w-9 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground",
          "group-hover:bg-primary/10 group-hover:text-primary",
        )}
      >
        <Icon size={18} />
      </span>
      <span className="min-w-0 flex-1 truncate text-sm font-medium text-foreground">{label}</span>
      <Icons.download
        size={14}
        className="shrink-0 text-muted-foreground opacity-0 transition-opacity group-hover:opacity-100"
        aria-hidden
      />
    </a>
  );
}

function extractExtensionFromLabel(label: string): string {
  const trimmed = label.replace(/\s+\([^)]*\)\s*$/, "").trim();
  return getFileExtension(trimmed);
}
