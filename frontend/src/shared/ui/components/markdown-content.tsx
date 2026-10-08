"use client";

import { isFileAssetHref } from "@/shared/lib/markdown-assets";
import { getNodeText } from "@/shared/lib/react/get-node-text";
import { APP_URL } from "@/shared/config/site";
import { FileAttachmentCard } from "@/shared/ui/components/file-attachment-card";
import { MarkdownCode } from "@/shared/ui/components/markdown-code";
import { Separator } from "@/shared/ui/kit/separator";
import { createHeadingSlugger } from "@/shared/lib/markdown-headings";
import { ContentImage } from "@/shared/ui/components/content-image";
import Link from "next/link";
import { type ReactNode } from "react";
import ReactMarkdown from "react-markdown";
import rehypeRaw from "rehype-raw";
import rehypeSanitize, { defaultSchema } from "rehype-sanitize";
import remarkGfm from "remark-gfm";

const REMARK_PLUGINS = [remarkGfm];

/**
 * #498: маппинг href из markdown на внутренний путь приложения. Относительные
 * (`/learn/...`) и абсолютные на собственный origin (APP_URL) ссылки — внутренние,
 * рендерятся Next `<Link>` в той же вкладке. Якоря (`#...`) и внешние URL — нет.
 */
function toInternalPath(href: string | undefined): string | null {
  if (!href || href.startsWith("#")) return null;
  if (href.startsWith("/")) return href;
  if (href.startsWith(`${APP_URL}/`) || href === APP_URL) {
    const path = href.slice(APP_URL.length);
    return path === "" ? "/" : path;
  }
  return null;
}

const sanitizeSchema = {
  ...defaultSchema,
  tagNames: [...(defaultSchema.tagNames ?? []), "img", "mark"],
  attributes: {
    ...defaultSchema.attributes,
    // srcset/sizes kept so the responsive `?w=` variants generated for embedded
    // images (buildImageMarkdown) survive sanitization.
    img: ["src", "alt", "width", "height", "loading", "decoding", "className", "srcset", "sizes"],
  },
};

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const REHYPE_PLUGINS = [rehypeRaw, [rehypeSanitize, sanitizeSchema]] as any[];

function ImageComponent({
  src,
  alt,
  width,
  height,
}: {
  src?: string;
  alt?: string;
  width?: number | string;
  height?: number | string;
}) {
  if (!src || src.trim() === "") {
    return (
      <span className="my-2 inline-block rounded-md border border-dashed border-muted-foreground/40 bg-muted/30 px-3 py-2 text-xs text-muted-foreground">
        {alt || "Загружается изображение…"}
      </span>
    );
  }
  // ContentImage builds the `?w=` srcSet from our `/files/{id}/content` src so
  // body images fetch a right-sized WebP variant; external images get a plain
  // src. width/height (from the generated <img>) are forwarded to reserve layout
  // space (CLS). The body renders at the article column width.
  return (
    <ContentImage
      src={src}
      alt={alt ?? ""}
      width={typeof width === "string" ? Number(width) || undefined : width}
      height={typeof height === "string" ? Number(height) || undefined : height}
      sizes="(min-width: 768px) 768px, 100vw"
      className="my-3 inline-block h-auto w-auto max-h-[480px] max-w-full rounded-lg border object-contain"
      loading="lazy"
    />
  );
}

function createBaseComponents({
  disableLinks,
  allowCodeCopy,
}: {
  disableLinks: boolean;
  allowCodeCopy: boolean;
}) {
  return {
    h1: ({ children }: { children?: ReactNode }) => (
      <h1 className="text-2xl font-bold text-foreground mt-6 mb-3">{children}</h1>
    ),
    h2: ({ children }: { children?: ReactNode }) => (
      <h2 className="text-xl font-bold text-foreground mt-6 mb-3">{children}</h2>
    ),
    h3: ({ children }: { children?: ReactNode }) => (
      <h3 className="text-lg font-semibold text-foreground mt-4 mb-2">{children}</h3>
    ),
    p: ({ children }: { children?: ReactNode }) => (
      <p className="text-base text-foreground/90 leading-7 my-5">{children}</p>
    ),
    ul: ({ children }: { children?: ReactNode }) => (
      <ul className="text-base text-foreground/90 leading-7 my-5 space-y-3 pl-6 list-disc marker:text-muted-foreground">
        {children}
      </ul>
    ),
    ol: ({ children }: { children?: ReactNode }) => (
      <ol className="text-base text-foreground/90 leading-7 my-5 space-y-3 pl-6 list-decimal marker:text-muted-foreground">
        {children}
      </ol>
    ),
    li: ({ children }: { children?: ReactNode }) => <li className="leading-7 pl-1">{children}</li>,
    strong: ({ children }: { children?: ReactNode }) => (
      <strong className="font-semibold text-foreground">{children}</strong>
    ),
    mark: ({ children }: { children?: ReactNode }) => (
      <mark className="rounded-[0.35rem] bg-yellow/15 px-1 py-0.5 font-medium text-foreground">
        {children}
      </mark>
    ),
    code: ({ children, className, ...props }: { children?: ReactNode; className?: string }) => (
      <MarkdownCode className={className} allowCopy={allowCodeCopy} {...props}>
        {children}
      </MarkdownCode>
    ),
    a: ({ href, children }: { href?: string; children?: ReactNode }) => {
      if (isFileAssetHref(href)) {
        return <FileAttachmentCard href={href ?? "#"} label={getNodeText(children) || "Файл"} />;
      }
      if (disableLinks) {
        return <span className="text-primary">{children}</span>;
      }
      // #498: SPA = одна вкладка. Внутренние ссылки контента (относительные или
      // на собственный origin) — client-side навигация без новой вкладки; новая
      // вкладка остаётся только для внешних источников.
      const internalPath = toInternalPath(href);
      if (internalPath) {
        return (
          <Link href={internalPath} className="text-primary hover:underline">
            {children}
          </Link>
        );
      }
      return (
        <a
          href={href}
          className="text-primary hover:underline"
          target="_blank"
          rel="noopener noreferrer"
        >
          {children}
        </a>
      );
    },
    img: ImageComponent,
    blockquote: ({ children }: { children?: ReactNode }) => (
      <div className="border-l-4 border-primary pl-4 my-5 text-muted-foreground italic text-base leading-7">
        {children}
      </div>
    ),
    table: ({ children }: { children?: ReactNode }) => (
      <div className="overflow-x-auto my-6 rounded-lg border border-border">
        <table className="w-full border-collapse text-sm">{children}</table>
      </div>
    ),
    thead: ({ children }: { children?: ReactNode }) => (
      <thead className="bg-secondary">{children}</thead>
    ),
    tr: ({ children }: { children?: ReactNode }) => <tr>{children}</tr>,
    th: ({ children }: { children?: ReactNode }) => (
      <th className="border border-border px-4 py-3 text-left font-semibold align-top">
        {children}
      </th>
    ),
    td: ({ children }: { children?: ReactNode }) => (
      <td className="border border-border px-4 py-3 align-top leading-6">{children}</td>
    ),
    hr: () => <Separator className="my-8" />,
  } as Record<string, React.ComponentType<Record<string, unknown>>>;
}

function createFullContentComponents({
  disableLinks,
  allowCodeCopy,
  enableHeadingIds,
}: {
  disableLinks: boolean;
  allowCodeCopy: boolean;
  enableHeadingIds: boolean;
}) {
  const slugger = createHeadingSlugger();

  function createHeadingComponent(Tag: "h1" | "h2" | "h3", className: string) {
    return function Heading({ children }: { children?: ReactNode }) {
      const text = getNodeText(children).trim();
      const id = enableHeadingIds && text ? slugger.next(text) : undefined;

      return (
        <Tag id={id} className={className}>
          {children}
        </Tag>
      );
    };
  }

  return {
    ...createBaseComponents({ disableLinks, allowCodeCopy }),
    h1: createHeadingComponent("h1", "text-3xl font-bold mt-8 mb-5 pb-3 border-b scroll-mt-24"),
    h2: createHeadingComponent("h2", "text-2xl font-bold mt-10 mb-4 scroll-mt-24"),
    h3: createHeadingComponent("h3", "text-xl font-semibold mt-6 mb-3 scroll-mt-24"),
  } as Record<string, React.ComponentType<Record<string, unknown>>>;
}

const FULL_CONTENT_COMPONENTS = createFullContentComponents({
  disableLinks: false,
  allowCodeCopy: true,
  enableHeadingIds: false,
});

const COMPACT_CONTENT_COMPONENTS = createBaseComponents({
  disableLinks: true,
  allowCodeCopy: false,
});

interface MarkdownContentProps {
  children: string;
  variant?: "compact" | "full";
  className?: string;
  disableLinks?: boolean;
  allowCodeCopy?: boolean;
  enableHeadingIds?: boolean;
}

export function MarkdownContent({
  children,
  variant = "full",
  className,
  disableLinks,
  allowCodeCopy,
  enableHeadingIds,
}: MarkdownContentProps) {
  const components =
    variant === "full"
      ? disableLinks === undefined && allowCodeCopy === undefined && enableHeadingIds === undefined
        ? FULL_CONTENT_COMPONENTS
        : createFullContentComponents({
            disableLinks: disableLinks ?? false,
            allowCodeCopy: allowCodeCopy ?? true,
            enableHeadingIds: enableHeadingIds ?? false,
          })
      : disableLinks === undefined && allowCodeCopy === undefined
        ? COMPACT_CONTENT_COMPONENTS
        : createBaseComponents({
            disableLinks: disableLinks ?? true,
            allowCodeCopy: allowCodeCopy ?? false,
          });

  return (
    <div className={className}>
      <ReactMarkdown
        remarkPlugins={REMARK_PLUGINS}
        rehypePlugins={REHYPE_PLUGINS}
        components={components}
      >
        {children}
      </ReactMarkdown>
    </div>
  );
}
