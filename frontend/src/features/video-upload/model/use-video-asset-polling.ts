import { videoApi, videoQueryOptions, type GetVideoResponse } from "@/entities/video";
import type { VideoInfo } from "@/shared/types";
import { useQuery } from "@tanstack/react-query";

const POLLING_INTERVAL = 2000;

export type UseVideoAssetPollingOptions = {
  /** Asset id to poll. When `null`, the hook disables itself. */
  assetId: string | null;
  initialVideo: VideoInfo | null;
  polling?: boolean;
  onReady?: () => void;
};

type UseVideoAssetPollingReturn = {
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

// Sibling of `useVideoPolling` — keyed on the asset id rather than a parent
// entity. Used by the material create form, where no entity exists yet but the
// upload (or attach-external) response gave us an asset id we can poll directly
// against `GET /videos/{id}`.
export function useVideoAssetPolling({
  assetId,
  initialVideo,
  polling = false,
  onReady,
}: UseVideoAssetPollingOptions): UseVideoAssetPollingReturn {
  const shouldPoll =
    Boolean(assetId) &&
    (polling || (initialVideo !== null && initialVideo.status !== "ready"));

  const { data: videoResponse, isFetching } = useQuery({
    queryKey: videoQueryOptions.byId(assetId ?? ""),
    queryFn: () => videoApi.getVideo(assetId ?? ""),
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
