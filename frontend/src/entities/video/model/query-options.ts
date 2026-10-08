import { queryOptions } from "@tanstack/react-query";
import { videoApi } from "../api";

export const videoQueryOptions = {
  baseKey: "videos",

  byEntity: (entityId: string, entityType: string) => [
    "videos",
    "by-entity",
    entityType,
    entityId,
  ],

  byId: (videoId: string) => ["videos", "by-id", videoId],
} as const;

export const videoByEntityQueryOptions = (
  entityId: string,
  entityType: string,
) =>
  queryOptions({
    queryKey: videoQueryOptions.byEntity(entityId, entityType),
    queryFn: () => videoApi.getVideoByEntity(entityId, entityType),
    enabled: !!entityId,
  });

export const videoChaptersQueryOptions = (videoId: string) =>
  queryOptions({
    queryKey: ["videos", "chapters", videoId] as const,
    queryFn: ({ signal }) => videoApi.getChapters(videoId, { signal }),
    enabled: !!videoId,
  });
