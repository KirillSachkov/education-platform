"use client";

import { requestGlobalSearchTag } from "@/shared/lib/global-search-store";
import type { ComponentProps } from "react";
import { TagsField } from "./tags-field";

export function SearchableTagsField(props: ComponentProps<typeof TagsField>) {
  return <TagsField {...props} onTagClick={requestGlobalSearchTag} />;
}
