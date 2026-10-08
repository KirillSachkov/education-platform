import { describe, expect, it } from "vitest";
import type { SearchEducationDocumentDto } from "@/entities/search";
import { EntityTypes } from "@/shared/config/entity-types";
import { searchDocumentToMaterialFeedItem } from "../search-to-material-feed";

function materialDoc(overrides: Partial<SearchEducationDocumentDto> = {}): SearchEducationDocumentDto {
  return {
    entityId: "mat-1",
    entityType: EntityTypes.MATERIAL,
    title: "Linux — практика. Часть 5",
    tagIds: [],
    tagTitles: [],
    updatedAtUtc: "2026-06-01T10:00:00Z",
    isAccessible: true,
    lockReason: null,
    materialKind: "VIDEO",
    ...overrides,
  };
}

describe("searchDocumentToMaterialFeedItem — cover resolution", () => {
  it("resolves a manual cover image (imageId) into the file-content URL", () => {
    const item = searchDocumentToMaterialFeedItem(
      materialDoc({ imageId: "cover-123", videoThumbnailUrl: null }),
    );
    expect(item.thumbnailUrl).toBe("/api/files/cover-123/content");
  });

  it("prefers the manual cover over the Kinescope poster (matches feed + Ctrl+K precedence)", () => {
    const item = searchDocumentToMaterialFeedItem(
      materialDoc({ imageId: "cover-123", videoThumbnailUrl: "https://kinescope/poster.jpg" }),
    );
    expect(item.thumbnailUrl).toBe("/api/files/cover-123/content");
  });

  it("falls back to the Kinescope poster when there is no manual cover", () => {
    const item = searchDocumentToMaterialFeedItem(
      materialDoc({ imageId: null, videoThumbnailUrl: "https://kinescope/poster.jpg" }),
    );
    expect(item.thumbnailUrl).toBe("https://kinescope/poster.jpg");
  });

  it("leaves thumbnailUrl null when neither cover nor poster exists", () => {
    const item = searchDocumentToMaterialFeedItem(
      materialDoc({ imageId: null, videoThumbnailUrl: null }),
    );
    expect(item.thumbnailUrl).toBeNull();
  });
});
