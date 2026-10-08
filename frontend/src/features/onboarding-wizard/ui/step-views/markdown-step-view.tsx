"use client";

import { MarkdownContentLazy } from "@/shared/ui/components/markdown-content-lazy";

type Props = { title: string; body: string };

export function MarkdownStepView({ title, body }: Props) {
  return (
    <article className="space-y-4">
      <h2 className="text-2xl font-semibold">{title}</h2>
      <MarkdownContentLazy variant="full">{body}</MarkdownContentLazy>
    </article>
  );
}
