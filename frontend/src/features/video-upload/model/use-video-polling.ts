import { videoApi, videoQueryOptions, type GetVideoResponse } from "@/entities/video";
import type { VideoInfo } from "@/shared/types";
import { useQuery } from "@tanstack/react-query";

const POLLING_INTERVAL = 2000;

export type UseVideoPollingOptions = {
  entityId: string;
  entityType: string;
  initialVideo: VideoInfo | null;
  polling?: boolean;
  onReady?: () => void;
};

type UseVideoPollingReturn = {
  video: VideoInfo | null;
  isPolling: boolean;
};

function mapResponseToVideoInfo(
  response: GetVideoResponse | null,
): VideoInfo | null {
  if (!response) return null;

  return {
    id: response.id,
    videoId: response.videoId,
    status: response.status,
    thumbnailUrl: response.thumbnailUrl,
    duration: response.duration,
    isOwnedStorage: response.isOwnedStorage,
  };
}

export function useVideoPolling({
  entityId,
  entityType,
  initialVideo,
  polling = false,
  onReady,
}: UseVideoPollingOptions): UseVideoPollingReturn {
  const shouldPoll =
    polling || (initialVideo !== null && initialVideo.status !== "ready");

  const { data: videoResponse, isFetching } = useQuery({
    queryKey: videoQueryOptions.byEntity(entityId, entityType),
    queryFn: async ({ signal }) => {
      const response = await videoApi.getVideoByEntity(entityId, entityType);
      void signal;
      return response;
    },
    refetchInterval: (query) => {
      if (!shouldPoll) return false;

      const data = query.state.data;
      if (data?.status === "ready") {
        onReady?.();
        return false;
      }

      return POLLING_INTERVAL;
    },
    enabled: shouldPoll,
    staleTime: 0,
  });

  const video =
    videoResponse !== undefined
      ? mapResponseToVideoInfo(videoResponse)
      : initialVideo;

  return {
    video,
    isPolling: shouldPoll && isFetching,
  };
}
