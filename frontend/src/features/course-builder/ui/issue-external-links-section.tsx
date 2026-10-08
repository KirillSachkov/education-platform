"use client";

import type { ExternalLinkItem, IssueExternalLinkDto } from "@/entities/issue";
import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Check, ExternalLink, Plus, Trash2, X } from "lucide-react";
import { useState } from "react";
import { useUpdateIssueLinks } from "../model/use-update-issue-links";

interface IssueExternalLinksSectionProps {
  issueId: string;
  projectId: string;
  links: IssueExternalLinkDto[];
}

export function IssueExternalLinksSection({
  issueId,
  projectId,
  links,
}: IssueExternalLinksSectionProps) {
  const { updateLinks, isPending } = useUpdateIssueLinks(issueId, projectId);
  const [adding, setAdding] = useState(false);
  const [newUrl, setNewUrl] = useState("");
  const [newTitle, setNewTitle] = useState("");

  const saveLinks = async (newItems: ExternalLinkItem[]) => {
    await updateLinks({ items: newItems });
  };

  const addLink = async () => {
    if (!newUrl.trim() || !newTitle.trim()) return;
    const newItems: ExternalLinkItem[] = [
      ...links.map((l) => ({
        url: l.url,
        title: l.title,
        isRequired: l.isRequired,
      })),
      { url: newUrl.trim(), title: newTitle.trim(), isRequired: false },
    ];
    await saveLinks(newItems);
    setNewUrl("");
    setNewTitle("");
    setAdding(false);
  };

  const removeLink = async (index: number) => {
    const newItems = links
      .filter((_, i) => i !== index)
      .map((l) => ({ url: l.url, title: l.title, isRequired: l.isRequired }));
    await saveLinks(newItems);
  };

  return (
    <div>
      <div className="flex items-center justify-between mb-3">
        <div className="flex items-center gap-1.5 text-sm font-medium">
          <ExternalLink size={14} className="text-muted-foreground" />
          Внешние ссылки
        </div>

        <Button
          type="button"
          variant="outline"
          size="icon"
          className="size-7"
          disabled={isPending}
          onClick={() => setAdding(true)}
        >
          <Plus size={14} />
        </Button>
      </div>

      {links.length === 0 && !adding && (
        <p className="text-sm text-muted-foreground">Нет внешних ссылок</p>
      )}

      <div className="space-y-1">
        {links.map((link, index) => (
          <div
            key={`${link.url}-${index}`}
            className="group flex items-center gap-2 px-3 py-2 rounded-lg bg-accent"
          >
            <ExternalLink size={13} className="text-muted-foreground shrink-0" />
            <div className="flex-1 min-w-0">
              <span className="text-sm font-medium truncate block">{link.title}</span>
              <span className="text-xs text-muted-foreground truncate block">{link.url}</span>
            </div>
            <button
              type="button"
              onClick={() => removeLink(index)}
              disabled={isPending}
              title="Удалить"
              className="size-7 rounded-md flex items-center justify-center text-muted-foreground hover:text-red hover:bg-red/10 transition-colors sm:opacity-0 sm:group-hover:opacity-100"
            >
              <Trash2 size={14} />
            </button>
          </div>
        ))}

        {adding && (
          <div className="flex flex-col gap-1.5 p-3 rounded-lg border border-dashed">
            <Input
              placeholder="Название"
              value={newTitle}
              onChange={(e) => setNewTitle(e.target.value)}
              className="h-8 text-sm"
              autoFocus
            />
            <Input
              placeholder="https://..."
              value={newUrl}
              onChange={(e) => setNewUrl(e.target.value)}
              className="h-8 text-sm"
            />
            <div className="flex items-center gap-1 justify-end">
              <Button
                type="button"
                variant="ghost"
                size="icon"
                className="size-7 text-muted-foreground"
                onClick={() => {
                  setAdding(false);
                  setNewUrl("");
                  setNewTitle("");
                }}
              >
                <X size={14} />
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="icon"
                className="size-7 text-teal"
                disabled={!newUrl.trim() || !newTitle.trim() || isPending}
                onClick={addLink}
              >
                <Check size={14} />
              </Button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
