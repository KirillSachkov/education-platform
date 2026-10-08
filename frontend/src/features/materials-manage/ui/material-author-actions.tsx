"use client";

import { type MaterialDetailDto, useChangeMaterialAccessType } from "@/entities/material";
import { routes } from "@/shared/config/routes";
import {
  MaterialAccessTypeMenuItems,
  MaterialAccessTypeSubmenu,
  getAccessTypeShortLabel,
} from "@/shared/ui/components";
import type { ContentAccessType } from "@/shared/ui/components";
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
  AlertDialogTrigger,
} from "@/shared/ui/kit/alert-dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/shared/ui/kit/dropdown-menu";
import { Archive, MoreVertical, Pencil, SendToBack, Trash2, Upload } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useArchiveMaterial } from "../model/use-archive-material";
import { useDeleteMaterial } from "../model/use-delete-material";
import { usePublishMaterial } from "../model/use-publish-material";
import { useSendMaterialToDraft } from "../model/use-send-material-to-draft";

interface MaterialAuthorActionsProps {
  material: MaterialDetailDto;
  /** When set, deleting the material will push to this path. Defaults to /author/knowledge-base. */
  onDeletedHref?: string;
  /** "editor" hides the Edit link (already editing); "viewer" shows all actions. */
  context?: "editor" | "viewer";
}

export function MaterialAuthorActions({
  material,
  onDeletedHref,
  context = "viewer",
}: MaterialAuthorActionsProps) {
  const router = useRouter();
  const { publishMaterial, isPending: isPublishPending } = usePublishMaterial();
  const { sendMaterialToDraft, isPending: isDraftPending } = useSendMaterialToDraft();
  const { archiveMaterial, isPending: isArchivePending } = useArchiveMaterial();
  const { deleteMaterial, isPending: isDeletePending } = useDeleteMaterial();
  const { setAccessType, isPending: isAccessPending } = useChangeMaterialAccessType();
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [publishOpen, setPublishOpen] = useState(false);
  const [notifyOnPublish, setNotifyOnPublish] = useState(true);

  const isMutating =
    isPublishPending || isDraftPending || isArchivePending || isDeletePending || isAccessPending;

  const handleDelete = async () => {
    await deleteMaterial(material.id);
    router.push(onDeletedHref ?? routes.authorKnowledgeBase);
  };

  const handlePublish = async () => {
    await publishMaterial({
      materialId: material.id,
      notifySubscribers: notifyOnPublish,
    });
    setPublishOpen(false);
  };

  const handleAccessChange = (next: ContentAccessType) => {
    void setAccessType(material.id, next);
  };

  const showPublish = material.status === "DRAFT" || material.status === "ARCHIVED";
  const showDraft = material.status === "PUBLISHED";
  const showArchive = material.status !== "ARCHIVED";
  const showEdit = context !== "editor";

  return (
    <>
      {/* ── Desktop: inline buttons ── */}
      <div className="hidden md:flex items-center gap-2">
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="outline" size="sm" disabled={isMutating}>
              Доступ: {getAccessTypeShortLabel(material.accessType)}
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="w-56">
            <MaterialAccessTypeMenuItems
              value={material.accessType}
              onChange={handleAccessChange}
              disabled={isMutating}
            />
          </DropdownMenuContent>
        </DropdownMenu>
        {showPublish && (
          <Button
            variant="outline"
            size="sm"
            disabled={isMutating}
            onClick={() => setPublishOpen(true)}
          >
            Опубликовать
          </Button>
        )}
        {showDraft && (
          <Button
            variant="outline"
            size="sm"
            disabled={isMutating}
            onClick={() => void sendMaterialToDraft(material.id)}
          >
            Вернуть в черновик
          </Button>
        )}
        {showArchive && (
          <Button
            variant="outline"
            size="sm"
            disabled={isMutating}
            onClick={() => void archiveMaterial(material.id)}
          >
            Архивировать
          </Button>
        )}
        <AlertDialog>
          <AlertDialogTrigger asChild>
            <Button
              variant="outline"
              size="sm"
              disabled={isMutating}
              className="text-destructive hover:bg-destructive/10 hover:text-destructive"
            >
              <Trash2 size={14} />
              Удалить
            </Button>
          </AlertDialogTrigger>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>Удалить материал?</AlertDialogTitle>
              <AlertDialogDescription>
                Материал «{material.title}» будет безвозвратно удалён.
                {material.courseCount > 0 && (
                  <>
                    {" "}
                    Он будет откреплён от <strong>{material.courseCount}</strong>{" "}
                    {material.courseCount === 1 ? "курса" : "курсов"}, всех модулей этих курсов и
                    подборок, где он используется.
                  </>
                )}{" "}
                Это действие нельзя отменить.
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
        {showEdit && (
          <Button
            asChild
            size="sm"
            className="border-0 bg-gradient-primary text-primary-foreground hover:opacity-90"
          >
            <Link href={routes.authorMaterialEdit(material.id)} prefetch={false}>
              <Pencil size={14} />
              Редактировать
            </Link>
          </Button>
        )}
      </div>

      {/* ── Mobile: compact dropdown ── */}
      <div className="flex md:hidden items-center gap-2">
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button
              variant="outline"
              size="sm"
              className="min-touch px-2"
              aria-label="Действия с материалом"
            >
              <MoreVertical size={16} />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="w-48">
            {showPublish && (
              <DropdownMenuItem disabled={isMutating} onClick={() => setPublishOpen(true)}>
                <Upload size={14} />
                Опубликовать
              </DropdownMenuItem>
            )}
            {showDraft && (
              <DropdownMenuItem
                disabled={isMutating}
                onClick={() => void sendMaterialToDraft(material.id)}
              >
                <SendToBack size={14} />
                Вернуть в черновик
              </DropdownMenuItem>
            )}
            {showArchive && (
              <DropdownMenuItem
                disabled={isMutating}
                onClick={() => void archiveMaterial(material.id)}
              >
                <Archive size={14} />
                Архивировать
              </DropdownMenuItem>
            )}
            <DropdownMenuSeparator />
            <MaterialAccessTypeSubmenu
              value={material.accessType}
              onChange={handleAccessChange}
              disabled={isMutating}
              triggerLabel={`Доступ: ${getAccessTypeShortLabel(material.accessType)}`}
            />
            <DropdownMenuSeparator />
            {showEdit && (
              <DropdownMenuItem asChild>
                <Link href={routes.authorMaterialEdit(material.id)} prefetch={false}>
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

      {/* Shared delete dialog for mobile dropdown */}
      <AlertDialog open={deleteOpen} onOpenChange={setDeleteOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Удалить материал?</AlertDialogTitle>
            <AlertDialogDescription>
              Материал «{material.title}» будет безвозвратно удалён. Это действие нельзя отменить.
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

      {/* Publish confirmation — checkbox controls whether subscribers of the course(s)
          this material belongs to are notified. Default: notify. Снимите галочку для
          мелких правок, чтобы не спамить. */}
      <AlertDialog open={publishOpen} onOpenChange={setPublishOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Опубликовать материал?</AlertDialogTitle>
            <AlertDialogDescription>
              Материал «{material.title}» станет виден ученикам.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <div className="flex items-center gap-2 py-2">
            <input
              id="notify-on-publish-material"
              type="checkbox"
              className="h-4 w-4 rounded border-border"
              checked={notifyOnPublish}
              onChange={(e) => setNotifyOnPublish(e.target.checked)}
            />
            <label htmlFor="notify-on-publish-material" className="text-sm select-none">
              Уведомить подписчиков курса
            </label>
          </div>
          <AlertDialogFooter>
            <AlertDialogCancel>Отмена</AlertDialogCancel>
            <AlertDialogAction
              onClick={(e) => {
                e.preventDefault();
                void handlePublish();
              }}
            >
              Опубликовать
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}
