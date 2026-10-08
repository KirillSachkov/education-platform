"use client";

import { Button } from "@/shared/ui/kit/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/shared/ui/kit/dialog";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { Megaphone } from "lucide-react";
import { useState } from "react";
import { useBroadcastNotification } from "../model/use-broadcast-notification";

interface BroadcastCourseDialogProps {
  courseId: string;
  courseTitle: string;
}

/**
 * Кнопка «Отправить объявление» для автора курса. Открывает модал с формой
 * (title / body) и отправляет <c>POST /notifications/broadcast/</c>
 * с <c>targetType: "course", targetId: courseId</c>.
 *
 * Каналы определяются backend'ом по шаблону AuthorAnnouncement — явно не шлём,
 * чтобы не зашивать UI в политику. Клик по уведомлению ведёт на backend-resolved targetUrl.
 */
export function BroadcastCourseDialog({ courseId, courseTitle }: BroadcastCourseDialogProps) {
  const { broadcast, isPending } = useBroadcastNotification();
  const [open, setOpen] = useState(false);
  const [title, setTitle] = useState("");
  const [body, setBody] = useState("");

  const canSubmit = title.trim().length > 0 && body.trim().length > 0;

  const handleSubmit = async () => {
    await broadcast({
      targetType: "course",
      targetId: courseId,
      title: title.trim(),
      body: body.trim(),
    });
    setTitle("");
    setBody("");
    setOpen(false);
  };

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button type="button" variant="outline" size="sm">
          <Megaphone size={14} />
          Отправить объявление
        </Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Рассылка подписчикам</DialogTitle>
          <DialogDescription>
            Сообщение получат все ученики, записанные на курс «{courseTitle}».
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="broadcast-title">Заголовок</Label>
            <Input
              id="broadcast-title"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              maxLength={200}
              placeholder="Например: «Перенос дедлайна»"
              disabled={isPending}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="broadcast-body">Текст</Label>
            <textarea
              id="broadcast-body"
              value={body}
              onChange={(e) => setBody(e.target.value)}
              rows={5}
              maxLength={5000}
              className="flex w-full rounded-md border border-input bg-transparent px-3 py-2 text-sm shadow-xs placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50"
              placeholder="Подробности сообщения"
              disabled={isPending}
            />
          </div>
        </div>

        <DialogFooter>
          <Button
            type="button"
            variant="outline"
            onClick={() => setOpen(false)}
            disabled={isPending}
          >
            Отмена
          </Button>
          <Button
            type="button"
            disabled={!canSubmit || isPending}
            onClick={() => void handleSubmit()}
          >
            {isPending ? "Отправка..." : "Отправить"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
