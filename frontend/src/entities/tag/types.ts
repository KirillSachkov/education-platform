import type { EntityType } from "@/shared/config/entity-types";

export type TagId = string;

export type TagKind = "canon" | "alias";

export type TagKindFilter = TagKind | "all";

export interface TagDto {
  id: TagId;
  title: string;
  slug: string;
  kind: TagKind;
}

export interface GetTagsFilters {
  cursor?: string | null;
  limit: number;
  search?: string;
  kind?: TagKind;
  authorId?: string;
}

export interface GetTagAliasesFilters {
  page?: number;
  pageSize?: number;
  search?: string;
}

export interface CreateTagRequest {
  title: string;
}

export interface UpdateTagRequest {
  title: string;
}

export interface GetEntityTagsFilters {
  entityType: EntityType;
  entityId: string;
  page?: number;
  pageSize?: number;
}

export interface SuggestTagsFilters {
  search?: string;
  authorId?: string;
  page?: number;
  pageSize?: number;
}

export interface GetPopularTagsFilters {
  limit?: number;
}

export interface AddTagsToEntityRequest {
  entityType: EntityType;
  entityId: string;
  tagIds?: TagId[];
  tagTitles?: string[];
}

export interface RemoveTagsFromEntityRequest {
  entityType: EntityType;
  entityId: string;
  tagIds: TagId[];
}

export interface MergeTagsRequest {
  tagIds: TagId[];
}

export interface RemoveAliasesRequest {
  tagIds: TagId[];
}
