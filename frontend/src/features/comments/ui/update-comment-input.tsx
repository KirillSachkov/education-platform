"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";

import type { CommentId } from "@/entities/comment";
import { commentFormSchema, type CommentFormValues } from "../model/schemas";
import { useUpdateComment } from "../model/use-update-comment";
import { CommentInput } from "./comment-input";

interface UpdateCommentInputProps {
  commentId: CommentId;
  defaultContent: string;
  onSuccess?: () => void;
  onCancel?: () => void;
}

export const UpdateCommentInput = ({
  commentId,
  defaultContent,
  onSuccess,
  onCancel,
}: UpdateCommentInputProps) => {
  const { updateComment, isPending } = useUpdateComment();

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<CommentFormValues>({
    resolver: zodResolver(commentFormSchema),
    defaultValues: { content: defaultContent },
  });

  const onSubmit = (data: CommentFormValues) => {
    updateComment({ id: commentId, content: data.content }, { onSuccess });
  };

  return (
    <form onSubmit={handleSubmit(onSubmit)}>
      <CommentInput
        {...register("content")}
        error={errors.content?.message}
        isPending={isPending}
        submitLabel="Сохранить"
        onCancel={onCancel}
        showAvatar={false}
      />
    </form>
  );
};
