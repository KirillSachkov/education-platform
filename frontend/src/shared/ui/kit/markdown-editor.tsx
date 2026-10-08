"use client";

import { cn } from "@/shared/lib/css";
import {
  createFailurePlaceholder,
  createFileFailurePlaceholder,
  createFileUploadPlaceholder,
  createUploadPlaceholder,
  isFileAssetHref,
  isImageFile,
} from "@/shared/lib/markdown-assets";
import { getNodeText } from "@/shared/lib/react/get-node-text";
import { FileAttachmentCard } from "@/shared/ui/components/file-attachment-card";
import { MarkdownCode } from "@/shared/ui/components/markdown-code";
import { Icons } from "@/shared/ui/icons";
import {
  Bold,
  Code,
  Heading2,
  Italic,
  Link,
  List,
  ListOrdered,
  Quote,
  Strikethrough,
} from "lucide-react";
import { ContentImage } from "@/shared/ui/components/content-image";
import { useEffect, useRef, useState } from "react";
import Markdown, { type Components } from "react-markdown";
import rehypeRaw from "rehype-raw";
import rehypeSanitize, { defaultSchema } from "rehype-sanitize";
import remarkGfm from "remark-gfm";

const sanitizeSchema = {
  ...defaultSchema,
  tagNames: [...(defaultSchema.tagNames ?? []), "img"],
  attributes: {
    ...defaultSchema.attributes,
    img: ["src", "alt", "width", "height", "loading", "decoding", "className"],
  },
};

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const rehypePlugins = [rehypeRaw, [rehypeSanitize, sanitizeSchema]] as any[];

interface MarkdownEditorProps {
  value?: string;
  onChange?: (value: string) => void;
  placeholder?: string;
  className?: string;
  disabled?: boolean;
  minHeight?: number;
  onImagePaste?: (file: File) => Promise<string | null>;
  onFileAttach?: (file: File) => Promise<string | null>;
  layout?: "tabs" | "split";
  defaultViewMode?: EditorViewMode;
  onViewModeChange?: (viewMode: EditorViewMode) => void;
}

type EditorViewMode = "write" | "split" | "preview";

type ToolbarAction = {
  icon: React.ReactNode;
  label: string;
  apply: (text: string, start: number, end: number) => { text: string; cursor: number };
};

const toolbarActions: ToolbarAction[] = [
  {
    icon: <Bold size={14} />,
    label: "Жирный",
    apply: (text, start, end) => {
      const selected = text.slice(start, end);
      const wrapped = `**${selected || "жирный текст"}**`;
      return {
        text: text.slice(0, start) + wrapped + text.slice(end),
        cursor: selected ? start + wrapped.length : start + 2,
      };
    },
  },
  {
    icon: <Italic size={14} />,
    label: "Курсив",
    apply: (text, start, end) => {
      const selected = text.slice(start, end);
      const wrapped = `*${selected || "курсив"}*`;
      return {
        text: text.slice(0, start) + wrapped + text.slice(end),
        cursor: selected ? start + wrapped.length : start + 1,
      };
    },
  },
  {
    icon: <Strikethrough size={14} />,
    label: "Зачёркнутый",
    apply: (text, start, end) => {
      const selected = text.slice(start, end);
      const wrapped = `~~${selected || "зачёркнутый"}~~`;
      return {
        text: text.slice(0, start) + wrapped + text.slice(end),
        cursor: selected ? start + wrapped.length : start + 2,
      };
    },
  },
  {
    icon: <Heading2 size={14} />,
    label: "Заголовок",
    apply: (text, start, end) => {
      const selected = text.slice(start, end);
      const lineStart = text.lastIndexOf("\n", start - 1) + 1;
      const prefix = "## ";
      const newText = text.slice(0, lineStart) + prefix + text.slice(lineStart);
      return {
        text: newText,
        cursor: selected ? start + prefix.length + (end - start) : start + prefix.length,
      };
    },
  },
  {
    icon: <Quote size={14} />,
    label: "Цитата",
    apply: (text, start, end) => {
      const selected = text.slice(start, end);
      const lineStart = text.lastIndexOf("\n", start - 1) + 1;
      const prefix = "> ";
      const newText = text.slice(0, lineStart) + prefix + text.slice(lineStart);
      return {
        text: newText,
        cursor: selected ? start + prefix.length + (end - start) : start + prefix.length,
      };
    },
  },
  {
    icon: <Code size={14} />,
    label: "Код",
    apply: (text, start, end) => {
      const selected = text.slice(start, end);
      if (selected.includes("\n")) {
        const wrapped = `\`\`\`\n${selected}\n\`\`\``;
        return {
          text: text.slice(0, start) + wrapped + text.slice(end),
          cursor: start + wrapped.length,
        };
      }
      const wrapped = `\`${selected || "код"}\``;
      return {
        text: text.slice(0, start) + wrapped + text.slice(end),
        cursor: selected ? start + wrapped.length : start + 1,
      };
    },
  },
  {
    icon: <Link size={14} />,
    label: "Ссылка",
    apply: (text, start, end) => {
      const selected = text.slice(start, end);
      const wrapped = `[${selected || "текст"}](url)`;
      return {
        text: text.slice(0, start) + wrapped + text.slice(end),
        cursor: selected ? start + selected.length + 3 : start + 1,
      };
    },
  },
  {
    icon: <List size={14} />,
    label: "Список",
    apply: (text, start) => {
      const lineStart = text.lastIndexOf("\n", start - 1) + 1;
      const prefix = "- ";
      const newText = text.slice(0, lineStart) + prefix + text.slice(lineStart);
      return { text: newText, cursor: start + prefix.length };
    },
  },
  {
    icon: <ListOrdered size={14} />,
    label: "Нумерованный список",
    apply: (text, start) => {
      const lineStart = text.lastIndexOf("\n", start - 1) + 1;
      const prefix = "1. ";
      const newText = text.slice(0, lineStart) + prefix + text.slice(lineStart);
      return { text: newText, cursor: start + prefix.length };
    },
  },
];

function MarkdownEditor({
  value = "",
  onChange,
  placeholder = "Начните писать...",
  className,
  disabled = false,
  minHeight = 200,
  onImagePaste,
  onFileAttach,
  layout = "tabs",
  defaultViewMode,
  onViewModeChange,
}: MarkdownEditorProps) {
  const resolvedDefaultViewMode = defaultViewMode ?? (layout === "split" ? "split" : "write");
  const [viewMode, setViewMode] = useState<EditorViewMode>(resolvedDefaultViewMode);
  const [isDraggingFiles, setIsDraggingFiles] = useState(false);
  const dragDepthRef = useRef(0);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const isSplitLayout = layout === "split";
  const canAttach = !disabled && (!!onImagePaste || !!onFileAttach);

  useEffect(() => {
    setViewMode(resolvedDefaultViewMode);
  }, [resolvedDefaultViewMode]);

  useEffect(() => {
    onViewModeChange?.(viewMode);
  }, [onViewModeChange, viewMode]);

  const applyAction = (action: ToolbarAction) => {
    const textarea = textareaRef.current;
    if (!textarea) return;

    const { selectionStart, selectionEnd } = textarea;
    const result = action.apply(value, selectionStart, selectionEnd);
    onChange?.(result.text);

    requestAnimationFrame(() => {
      textarea.focus();
      textarea.setSelectionRange(result.cursor, result.cursor);
    });
  };

  const latestValueRef = useRef(value);
  useEffect(() => {
    latestValueRef.current = value;
  });

  const insertAttachmentAtCursor = async (
    file: File,
    handler: (file: File) => Promise<string | null>,
    placeholderBuilder: (fileName: string) => string,
    failureBuilder: (fileName: string) => string,
  ) => {
    const textarea = textareaRef.current;
    const cursor = textarea?.selectionStart ?? latestValueRef.current.length;
    const placeholder = placeholderBuilder(file.name);

    const before = latestValueRef.current.slice(0, cursor);
    const after = latestValueRef.current.slice(cursor);
    onChange?.(before + placeholder + after);

    const inserted = await handler(file);
    const replacement = inserted ?? failureBuilder(file.name);
    onChange?.(latestValueRef.current.replace(placeholder, replacement));
  };

  const dispatchUpload = async (file: File) => {
    if (isImageFile(file)) {
      if (!onImagePaste) return;
      await insertAttachmentAtCursor(
        file,
        onImagePaste,
        createUploadPlaceholder,
        createFailurePlaceholder,
      );
      return;
    }
    if (onFileAttach) {
      await insertAttachmentAtCursor(
        file,
        onFileAttach,
        createFileUploadPlaceholder,
        createFileFailurePlaceholder,
      );
    }
  };

  const handlePaste = async (e: React.ClipboardEvent<HTMLTextAreaElement>) => {
    if (!canAttach) return;

    const items = e.clipboardData?.items;
    if (!items) return;

    const files: File[] = [];
    for (const item of Array.from(items)) {
      if (item.kind !== "file") continue;
      const file = item.getAsFile();
      if (file) files.push(file);
    }
    if (files.length === 0) return;

    e.preventDefault();
    for (const file of files) {
      await dispatchUpload(file);
    }
  };

  const handleDragEnter = (e: React.DragEvent<HTMLDivElement>) => {
    if (!canAttach) return;
    if (!e.dataTransfer.types.includes("Files")) return;
    e.preventDefault();
    dragDepthRef.current += 1;
    setIsDraggingFiles(true);
  };

  const handleDragOver = (e: React.DragEvent<HTMLDivElement>) => {
    if (!canAttach) return;
    if (!e.dataTransfer.types.includes("Files")) return;
    e.preventDefault();
    e.dataTransfer.dropEffect = "copy";
  };

  const handleDragLeave = (e: React.DragEvent<HTMLDivElement>) => {
    if (!canAttach) return;
    e.preventDefault();
    dragDepthRef.current = Math.max(0, dragDepthRef.current - 1);
    if (dragDepthRef.current === 0) {
      setIsDraggingFiles(false);
    }
  };

  const handleDrop = async (e: React.DragEvent<HTMLDivElement>) => {
    if (!canAttach) return;
    e.preventDefault();
    dragDepthRef.current = 0;
    setIsDraggingFiles(false);

    const files = Array.from(e.dataTransfer.files ?? []);
    if (files.length === 0) return;

    for (const file of files) {
      await dispatchUpload(file);
    }
  };

  const handleAttachClick = () => {
    fileInputRef.current?.click();
  };

  const handleFileInputChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(e.target.files ?? []);
    e.target.value = "";
    for (const file of files) {
      await dispatchUpload(file);
    }
  };

  const renderPreview = () => (
    <div className="min-w-0 flex-1 overflow-auto p-4 break-words [overflow-wrap:anywhere]">
      {value ? (
        <Markdown
          remarkPlugins={[remarkGfm]}
          rehypePlugins={rehypePlugins}
          components={previewComponents}
        >
          {value}
        </Markdown>
      ) : (
        <p className="text-sm text-muted-foreground italic">Нет содержимого для предпросмотра</p>
      )}
    </div>
  );

  return (
    <div
      className={cn(
        "relative flex min-h-0 w-full max-w-full flex-col overflow-hidden rounded-lg border",
        isDraggingFiles && "border-primary ring-2 ring-primary/30",
        className,
      )}
      onDragEnter={handleDragEnter}
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
    >
      <div className="flex flex-wrap items-center gap-y-2 border-b bg-muted/50 px-1 py-1">
        {isSplitLayout ? (
          <div className="flex items-center gap-0.5">
            <button
              type="button"
              className={cn(
                "px-2.5 py-1 text-xs font-medium rounded transition-colors",
                viewMode === "write"
                  ? "bg-background text-foreground shadow-sm"
                  : "text-muted-foreground hover:text-foreground",
              )}
              onClick={() => setViewMode("write")}
            >
              Markdown
            </button>
            <button
              type="button"
              className={cn(
                "px-2.5 py-1 text-xs font-medium rounded transition-colors",
                viewMode === "split"
                  ? "bg-background text-foreground shadow-sm"
                  : "text-muted-foreground hover:text-foreground",
              )}
              onClick={() => setViewMode("split")}
            >
              Split
            </button>
            <button
              type="button"
              className={cn(
                "px-2.5 py-1 text-xs font-medium rounded transition-colors",
                viewMode === "preview"
                  ? "bg-background text-foreground shadow-sm"
                  : "text-muted-foreground hover:text-foreground",
              )}
              onClick={() => setViewMode("preview")}
            >
              Preview
            </button>
          </div>
        ) : (
          <div className="flex items-center gap-0.5">
            <button
              type="button"
              className={cn(
                "px-2.5 py-1 text-xs font-medium rounded transition-colors",
                viewMode === "write"
                  ? "bg-background text-foreground shadow-sm"
                  : "text-muted-foreground hover:text-foreground",
              )}
              onClick={() => setViewMode("write")}
            >
              Написать
            </button>
            <button
              type="button"
              className={cn(
                "px-2.5 py-1 text-xs font-medium rounded transition-colors",
                viewMode === "preview"
                  ? "bg-background text-foreground shadow-sm"
                  : "text-muted-foreground hover:text-foreground",
              )}
              onClick={() => setViewMode("preview")}
            >
              Превью
            </button>
          </div>
        )}

        {!disabled && (
          <>
            <div className="w-px h-4 bg-border mx-2" />
            <div className="flex flex-wrap items-center gap-0.5">
              {toolbarActions.map((action) => (
                <button
                  key={action.label}
                  type="button"
                  title={action.label}
                  className="p-1.5 rounded text-muted-foreground hover:text-foreground hover:bg-accent transition-colors"
                  onClick={() => applyAction(action)}
                >
                  {action.icon}
                </button>
              ))}
              {canAttach && (
                <>
                  <div className="w-px h-4 bg-border mx-1" />
                  <button
                    type="button"
                    title="Прикрепить файл"
                    onClick={handleAttachClick}
                    className="p-1.5 rounded text-muted-foreground hover:text-foreground hover:bg-accent transition-colors"
                  >
                    <Icons.paperclip size={14} />
                  </button>
                  <input
                    ref={fileInputRef}
                    type="file"
                    multiple
                    className="hidden"
                    onChange={handleFileInputChange}
                  />
                </>
              )}
            </div>
          </>
        )}
      </div>

      {isSplitLayout && viewMode === "split" ? (
        <div
          className="grid min-h-0 flex-1 grid-cols-1 grid-rows-[minmax(0,1fr)_minmax(0,1fr)] divide-y divide-border xl:grid-cols-[minmax(0,1fr)_minmax(0,1fr)] xl:grid-rows-1 xl:divide-x xl:divide-y-0"
          style={{ minHeight }}
        >
          <div className="flex min-h-0 min-w-0 flex-col bg-background">
            <div className="border-b border-border/70 px-4 py-2 text-xs font-medium uppercase tracking-[0.16em] text-muted-foreground">
              Markdown
            </div>
            <textarea
              ref={textareaRef}
              value={value}
              onChange={(e) => onChange?.(e.target.value)}
              onPaste={handlePaste}
              placeholder={placeholder}
              disabled={disabled}
              className="min-h-[18rem] min-w-0 flex-1 resize-none overflow-auto bg-background p-4 font-mono text-sm leading-relaxed text-foreground whitespace-pre-wrap outline-none placeholder:text-muted-foreground [overflow-wrap:anywhere] disabled:cursor-not-allowed disabled:opacity-50 xl:min-h-0"
            />
          </div>

          <div className="flex min-h-0 min-w-0 flex-col bg-background/60">
            <div className="border-b border-border/70 px-4 py-2 text-xs font-medium uppercase tracking-[0.16em] text-muted-foreground">
              Live preview
            </div>
            {renderPreview()}
          </div>
        </div>
      ) : (
        <>
          {viewMode === "write" ? (
            <textarea
              ref={textareaRef}
              value={value}
              onChange={(e) => onChange?.(e.target.value)}
              onPaste={handlePaste}
              placeholder={placeholder}
              disabled={disabled}
              style={{ minHeight }}
              className="min-w-0 flex-1 resize-none overflow-auto bg-background p-4 font-mono text-sm leading-relaxed text-foreground whitespace-pre-wrap outline-none placeholder:text-muted-foreground [overflow-wrap:anywhere] disabled:cursor-not-allowed disabled:opacity-50"
            />
          ) : (
            <div className="min-h-0 flex-1 bg-background" style={{ minHeight }}>
              {renderPreview()}
            </div>
          )}
        </>
      )}

      {isDraggingFiles && (
        <div className="pointer-events-none absolute inset-0 flex items-center justify-center bg-primary/10 backdrop-blur-[1px]">
          <div className="flex items-center gap-2 rounded-md border border-primary/40 bg-background/95 px-4 py-2 text-sm font-medium text-primary shadow">
            <Icons.paperclip size={16} />
            Отпустите, чтобы прикрепить файл
          </div>
        </div>
      )}
    </div>
  );
}

const previewComponents: Components = {
  p: (props) => <p className="text-sm leading-relaxed my-4 break-words" {...props} />,
  h1: (props) => <h1 className="text-xl font-bold mt-6 mb-3 pb-2 border-b" {...props} />,
  h2: (props) => <h2 className="text-lg font-semibold mt-6 mb-3" {...props} />,
  h3: (props) => <h3 className="text-base font-semibold mt-5 mb-2" {...props} />,
  ul: (props) => (
    <ul
      className="text-sm leading-relaxed my-4 space-y-2 pl-5 list-disc marker:text-muted-foreground"
      {...props}
    />
  ),
  ol: (props) => (
    <ol
      className="text-sm leading-relaxed my-4 space-y-2 pl-5 list-decimal marker:text-muted-foreground"
      {...props}
    />
  ),
  li: (props) => <li className="leading-relaxed pl-1" {...props} />,
  strong: (props) => <strong className="font-semibold text-foreground" {...props} />,
  table: ({ children }) => (
    <div className="overflow-x-auto my-5 rounded-lg border border-border">
      <table className="w-full border-collapse text-sm">{children}</table>
    </div>
  ),
  thead: ({ children }) => <thead className="bg-secondary">{children}</thead>,
  th: ({ children }) => (
    <th className="border border-border px-3 py-2.5 text-left font-semibold align-top">
      {children}
    </th>
  ),
  td: ({ children }) => (
    <td className="border border-border px-3 py-2.5 align-top leading-6">{children}</td>
  ),
  hr: () => <hr className="my-6 border-border" />,
  code: ({ children, className, ...props }) => (
    <MarkdownCode className={className} allowCopy {...props}>
      {children}
    </MarkdownCode>
  ),
  blockquote: ({ children }) => (
    <div className="border-l-4 border-primary pl-4 my-3 text-muted-foreground italic text-sm">
      {children}
    </div>
  ),
  a: ({ href, children }) => {
    if (isFileAssetHref(href)) {
      return <FileAttachmentCard href={href ?? "#"} label={getNodeText(children) || "Файл"} />;
    }
    // Превью РЕДАКТОРА: любая ссылка — в новой вкладке намеренно (исключение из
    // правила «SPA = одна вкладка», #498) — переход в той же вкладке терял бы
    // недосохранённый черновик. Студенческий рендер (markdown-content.tsx)
    // открывает внутренние ссылки в той же вкладке.
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
  img: ({ src, alt }) => {
    if (typeof src !== "string" || src.trim() === "") {
      return (
        <span className="my-2 inline-block rounded-md border border-dashed border-muted-foreground/40 bg-muted/30 px-3 py-2 text-xs text-muted-foreground">
          {alt || "Загружается изображение…"}
        </span>
      );
    }
    return (
      <ContentImage
        src={src}
        alt={alt ?? ""}
        sizes="(min-width: 768px) 768px, 100vw"
        className="my-3 inline-block h-auto w-auto max-h-[420px] max-w-full rounded-lg border object-contain"
        loading="lazy"
      />
    );
  },
};

export { MarkdownEditor };
