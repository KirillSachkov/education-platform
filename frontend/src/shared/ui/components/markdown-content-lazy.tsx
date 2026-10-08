import dynamic from "next/dynamic";

export const MarkdownContentLazy = dynamic(
  () =>
    import("./markdown-content").then((m) => ({
      default: m.MarkdownContent,
    })),
  {
    loading: () => (
      <div className="animate-pulse bg-muted rounded h-32" />
    ),
  },
);
