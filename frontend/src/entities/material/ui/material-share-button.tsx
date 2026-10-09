"use client";

import type { ComponentProps } from "react";
import { ShareButton } from "@/shared/ui/components";
import type { MaterialId } from "../types";

type MaterialShareButtonProps = ComponentProps<typeof ShareButton> & { materialId: MaterialId };

export function MaterialShareButton({
  materialId: _materialId,
  ...shareProps
}: MaterialShareButtonProps) {
  return <ShareButton {...shareProps} />;
}
