export { bookmarksApi, bookmarkQueryOptions } from "./api";
export { useBookmarkToggle } from "./model/use-bookmark-toggle";
export { useBookmarkStatus } from "./model/use-bookmark-status";
export { BookmarkStatusProvider } from "./model/bookmark-status-context";
export { BookmarkToggleButton } from "./ui/bookmark-toggle-button";
export type {
  BookmarkIdDto,
  BookmarkedMaterialDto,
  BookmarkTargetType,
  EntityReferenceDto,
  GetBookmarksRequest,
  GetMyBookmarkIdsRequest,
} from "./types";
