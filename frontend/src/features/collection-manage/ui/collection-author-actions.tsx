"use client";

import type { CollectionDetailDto } from "@/entities/collection";
import { routes } from "@/shared/config/routes";
import { Button } from "@/shared/ui/kit/button";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/shared/ui/kit/alert-dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/shared/ui/kit/dropdown-menu";
import {
  Archive,
  MoreVertical,
  Pencil,
  SendToBack,
  Trash2,
  Upload,
} from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useArchiveCollection } from "../model/use-archive-collection";
import { useDeleteCollection } from "../model/use-delete-collection";
import { usePublishCollection } from "../model/use-publish-collection";
import { useSendCollectionToDraft } from "../model/use-send-collection-to-draft";

interface CollectionAuthorActionsProps {
  collection: CollectionDetailDto;
  /** "editor" hides the Edit link (already editing); "viewer" shows all actions. */
  context?: "editor" | "viewer";
}

export function CollectionAuthorActions({
  collection,
  context = "viewer",
}: CollectionAuthorActionsProps) {
  const router = useRouter();
  const { publishCollection, isPending: isPublishPending } =
    usePublishCollection();
  const { sendCollectionToDraft, isPending: isDraftPending } =
    useSendCollectionToDraft();
  const { archiveCollection, isPending: isArchivePending } =
    useArchiveCollection();
  const { deleteCollection, isPending: isDeletePending } =
    useDeleteCollection();
  const [deleteOpen, setDeleteOpen] = useState(false);

  const isMutating =
    isPublishPending || isDraftPending || isArchivePending || isDeletePending;

  const handleDelete = async () => {
    await deleteCollection(collection.id);
    router.push(routes.authorCollections);
  };

  const showPublish =
    collection.status === "DRAFT" || collection.status === "ARCHIVED";
  const showDraft = collection.status === "PUBLISHED";
  const showArchive = collection.status === "PUBLISHED";
  const showEdit = context !== "editor";

  return (
    <>
      {/* Desktop: inline buttons */}
      <div className="hidden md:flex items-center gap-2">
        {showPublish && (
          <Button
            variant="outline"
            size="sm"
            disabled={isMutating}
            onClick={() => void publishCollection(collection.id)}
          >
            Опубликовать
          </Button>
        )}
        {showDraft && (
          <Button
            variant="outline"
            size="sm"
            disabled={isMutating}
            onClick={() => void sendCollectionToDraft(collection.id)}
          >
            Вернуть в черновик
          </Button>
        )}
        {showArchive && (
          <Button
            variant="outline"
            size="sm"
            disabled={isMutating}
            onClick={() => void archiveCollection(collection.id)}
          >
            Архивировать
          </Button>
        )}
        <Button
          variant="outline"
          size="sm"
          disabled={isMutating}
          className="text-destructive hover:bg-destructive/10 hover:text-destructive"
          onClick={() => setDeleteOpen(true)}
        >
          <Trash2 size={14} />
          Удалить
        </Button>
        {showEdit && (
          <Button
            asChild
            size="sm"
            className="border-0 bg-gradient-primary text-primary-foreground hover:opacity-90"
          >
            <Link href={routes.authorCollectionEdit(collection.id)} prefetch={false}>
              <Pencil size={14} />
              Редактировать
            </Link>
          </Button>
        )}
      </div>

      {/* Mobile: compact dropdown */}
      <div className="flex md:hidden items-center gap-2">
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="outline" size="sm" className="px-2">
              <MoreVertical size={16} />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="w-48">
            {showPublish && (
              <DropdownMenuItem
                disabled={isMutating}
                onClick={() => void publishCollection(collection.id)}
              >
                <Upload size={14} />
                Опубликовать
              </DropdownMenuItem>
            )}
            {showDraft && (
              <DropdownMenuItem
                disabled={isMutating}
                onClick={() => void sendCollectionToDraft(collection.id)}
              >
                <SendToBack size={14} />
                Вернуть в черновик
              </DropdownMenuItem>
            )}
            {showArchive && (
              <DropdownMenuItem
                disabled={isMutating}
                onClick={() => void archiveCollection(collection.id)}
              >
                <Archive size={14} />
                Архивировать
              </DropdownMenuItem>
            )}
            <DropdownMenuSeparator />
            {showEdit && (
              <DropdownMenuItem asChild>
                <Link href={routes.authorCollectionEdit(collection.id)} prefetch={false}>
                  <Pencil size={14} />
                  Редактировать
                </Link>
              </DropdownMenuItem>
            )}
            <DropdownMenuItem
              disabled={isMutating}
              className="text-destructive focus:text-destructive"
              onClick={() => setDeleteOpen(true)}
            >
              <Trash2 size={14} />
              Удалить
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

      {/* Shared delete dialog */}
      <AlertDialog open={deleteOpen} onOpenChange={setDeleteOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Удалить подборку?</AlertDialogTitle>
            <AlertDialogDescription>
              Подборка &laquo;{collection.title}&raquo; будет безвозвратно
              удалена. Это действие нельзя отменить.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Отмена</AlertDialogCancel>
            <AlertDialogAction
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
              onClick={(e) => {
                e.preventDefault();
                void handleDelete();
              }}
            >
              Удалить
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}
