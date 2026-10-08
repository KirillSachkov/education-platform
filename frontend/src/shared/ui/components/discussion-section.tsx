"use client";

import { Avatar, AvatarFallback } from "@/shared/ui/kit/avatar";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { Textarea } from "@/shared/ui/kit/textarea";
import { MessageSquare, Send } from "lucide-react";
import { useState } from "react";

interface Comment {
  id: string;
  userName: string;
  avatar: string;
  text: string;
  time: string;
}

interface DiscussionSectionProps {
  initialComments?: Comment[];
  currentUserAvatar?: string;
  placeholder?: string;
}

const DEFAULT_COMMENTS: Comment[] = [
  {
    id: "c1",
    userName: "Мария Иванова",
    avatar: "МИ",
    text: "Какую библиотеку лучше использовать для HTTP запросов?",
    time: "3 дня назад",
  },
  {
    id: "c2",
    userName: "Дмитрий Ларин",
    avatar: "ДЛ",
    text: "Я использовал fetch API и всё работает. Главное — правильно типизировать ответ.",
    time: "2 дня назад",
  },
];

export function DiscussionSection({
  initialComments = DEFAULT_COMMENTS,
  currentUserAvatar = "АП",
  placeholder = "Задайте вопрос или поделитесь мыслями...",
}: DiscussionSectionProps) {
  const [newComment, setNewComment] = useState("");
  const [comments, setComments] = useState<Comment[]>(initialComments);

  const submitComment = () => {
    if (!newComment.trim()) return;
    setComments((prev) => [
      ...prev,
      {
        id: `c${Date.now()}-${Math.random().toString(36).slice(2, 6)}`,
        userName: "Алексей Петров",
        avatar: currentUserAvatar,
        text: newComment.trim(),
        time: "только что",
      },
    ]);
    setNewComment("");
  };

  return (
    <div className="space-y-4">
      <h3 className="text-sm font-semibold flex items-center gap-2">
        <MessageSquare size={14} className="text-muted-foreground" />
        Обсуждение
        <Badge variant="secondary" className="text-xs font-normal">
          {comments.length}
        </Badge>
      </h3>

      <div className="flex gap-3">
        <Avatar className="size-8 shrink-0">
          <AvatarFallback className="bg-gradient-primary text-2xs font-bold text-primary-foreground">
            {currentUserAvatar}
          </AvatarFallback>
        </Avatar>
        <div className="flex-1">
          <Textarea
            value={newComment}
            onChange={(e) => setNewComment(e.target.value)}
            placeholder={placeholder}
            rows={3}
            className="resize-none"
          />
          <div className="flex justify-end mt-2">
            <Button
              size="sm"
              onClick={submitComment}
              disabled={!newComment.trim()}
            >
              <Send size={12} /> Отправить
            </Button>
          </div>
        </div>
      </div>

      <div className="space-y-4">
        {comments.map((comment) => (
          <div key={comment.id} className="flex gap-3">
            <Avatar className="size-8 shrink-0">
              <AvatarFallback className="text-2xs font-bold">
                {comment.avatar}
              </AvatarFallback>
            </Avatar>
            <Card className="flex-1 p-4 gap-2">
              <div className="flex items-center gap-2">
                <span className="text-sm font-semibold">
                  {comment.userName}
                </span>
                <span className="text-xs text-muted-foreground">
                  {comment.time}
                </span>
              </div>
              <p className="text-sm text-foreground/85 leading-relaxed">
                {comment.text}
              </p>
            </Card>
          </div>
        ))}
      </div>
    </div>
  );
}
