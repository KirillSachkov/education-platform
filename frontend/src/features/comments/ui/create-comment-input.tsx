"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";

import type { TargetEntity } from "@/entities/comment";
import { commentFormSchema, type CommentFormValues } from "../model/schemas";
import { useCreateComment } from "../model/use-create-comment";
import { CommentInput } from "./comment-input";

interface CreateCommentInputProps {
  targetEntity: TargetEntity;
  parentId?: string;
  onSuccess?: () => void;
  onCancel?: () => void;
  placeholder?: string;
  userName?: string | null | undefined;
  userAvatarId?: string | null | undefined;
  autoFocus?: boolean;
}

export const CreateCommentInput = ({
  targetEntity,
  parentId,
  onSuccess,
  onCancel,
  placeholder,
  userName,
  userAvatarId,
  autoFocus,
}: CreateCommentInputProps) => {
  const { createComment, isPending } = useCreateComment();
  const [inputKey, setInputKey] = useState(0);

  const defaultValues: CommentFormValues = { content: "" };

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<CommentFormValues>({
    resolver: zodResolver(commentFormSchema),
    defaultValues,
  });

  const onSubmit = (data: CommentFormValues) => {
    createComment(
      {
        entityReference: targetEntity,
        content: data.content,
        ...(parentId ? { parentId } : {}),
      },
      {
        onSuccess: () => {
          reset(defaultValues);
          setInputKey((key) => key + 1);
          onSuccess?.();
        },
      },
    );
  };

  return (
    <form onSubmit={handleSubmit(onSubmit)}>
      <CommentInput
        key={inputKey}
        {...register("content")}
        error={errors.content?.message}
        isPending={isPending}
        placeholder={placeholder}
        autoFocus={autoFocus}
        onCancel={onCancel}
        userName={userName}
        userAvatarId={userAvatarId}
      />
    </form>
  );
};
