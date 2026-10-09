"use client";

import { videoApi, videoChaptersQueryOptions, type UpdateVideoChapterItem } from "@/entities/video";
import { getErrorMessage } from "@/shared/api";
import { Button } from "@/shared/ui/kit/button";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { TimecodesEditor } from "./timecodes-editor";

export function VideoChaptersEditor({ videoId }: { videoId: string }) {
  const queryClient = useQueryClient();
  const chaptersQuery = useQuery(videoChaptersQueryOptions(videoId));
  const saveChapters = useMutation({
    mutationFn: (chapters: UpdateVideoChapterItem[]) =>
      videoApi.replaceChapters({ videoId, request: { chapters } }),
    onSuccess: (response) => {
      queryClient.setQueryData(videoChaptersQueryOptions(videoId).queryKey, response);
      toast.success("Главы сохранены в Kinescope");
    },
  });

  if (chaptersQuery.isPending) {
    return (
      <p role="status" className="text-sm text-muted-foreground">
        Загружаем главы…
      </p>
    );
  }

  if (!chaptersQuery.data) {
    return (
      <div role="alert" className="space-y-2 text-sm">
        <p>{getErrorMessage(chaptersQuery.error, "Не удалось загрузить главы")}</p>
        <Button
          type="button"
          variant="outline"
          className="min-touch"
          onClick={() => void chaptersQuery.refetch()}
        >
          Повторить загрузку глав
        </Button>
      </div>
    );
  }

  return (
    <TimecodesEditor
      key={videoId}
      timecodes={chaptersQuery.data.chapters}
      isSaving={saveChapters.isPending}
      onSave={async (chapters) => {
        await saveChapters.mutateAsync(chapters);
      }}
    />
  );
}
