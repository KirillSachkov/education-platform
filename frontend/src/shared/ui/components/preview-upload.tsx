"use client";

import { resolveImageUrl } from "@/shared/lib/image-src";
import { getFirstFile, getFirstFileFromClipboard } from "@/shared/lib/file-transfer";
import { ContentImage } from "@/shared/ui/components/content-image";
import { ImageCropperDialog } from "@/shared/ui/components/image-cropper-dialog";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { useRef, useState } from "react";

const ACCEPT = "image/jpeg,image/png,image/webp";

type PreviewUploadHookResult = {
  upload: (file: File) => void;
  isUploading: boolean;
  previewUrl: string | null;
  uploadedAssetId: string | null;
  isDeleted: boolean;
  remove: (imageId: string) => void;
  isRemoving: boolean;
};

export type PreviewAspectRatio = "video" | "portrait";

type Props = {
  hook: PreviewUploadHookResult;
  imageId?: string | null;
  initialPreviewUrl?: string | null;
  alt: string;
  /**
   * `video` — 16:9 (default; курсы/материалы).
   * `portrait` — 3:4 (подборки).
   */
  aspectRatio?: PreviewAspectRatio;
};

const ASPECT_NUMBERS: Record<PreviewAspectRatio, number> = {
  video: 16 / 9,
  portrait: 3 / 4,
};

const ASPECT_CLASSES: Record<PreviewAspectRatio, string> = {
  video: "aspect-video",
  portrait: "aspect-[3/4]",
};

const CROP_HINTS: Record<PreviewAspectRatio, string> = {
  video: "Обложка 16:9 — рекомендуем 1280×720 и крупнее.",
  portrait: "Обложка 3:4 — рекомендуем 900×1200 и крупнее.",
};

const FILLED_MAX_WIDTH: Record<PreviewAspectRatio, string> = {
  video: "max-w-2xl",
  portrait: "max-w-xs",
};

export function PreviewUpload({
  hook,
  imageId,
  initialPreviewUrl,
  alt,
  aspectRatio = "video",
}: Props) {
  const { upload, isUploading, previewUrl, uploadedAssetId, isDeleted, remove, isRemoving } = hook;

  const displayUrl =
    resolveImageUrl(previewUrl) ?? (isDeleted ? null : resolveImageUrl(initialPreviewUrl));
  const inputRef = useRef<HTMLInputElement>(null);
  const [pendingCrop, setPendingCrop] = useState<File | null>(null);

  const currentImageId = uploadedAssetId ?? imageId;
  const aspectClass = ASPECT_CLASSES[aspectRatio];

  const handleRemove = () => {
    if (currentImageId) {
      remove(currentImageId);
    }
  };

  const handleSelectedFile = (file: File) => {
    if (file.type && !file.type.startsWith("image/")) {
      upload(file);
      return;
    }

    setPendingCrop(file);
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = getFirstFile(e.target.files);
    if (file) {
      handleSelectedFile(file);
    }
    if (inputRef.current) {
      inputRef.current.value = "";
    }
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    const file = getFirstFile(e.dataTransfer.files);
    if (file) {
      handleSelectedFile(file);
    }
  };

  const handlePaste = (e: React.ClipboardEvent) => {
    const file = getFirstFileFromClipboard(e.clipboardData);
    if (file) {
      e.preventDefault();
      handleSelectedFile(file);
    }
  };

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    e.dataTransfer.dropEffect = "copy";
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (e.key !== "Enter" && e.key !== " ") return;
    e.preventDefault();
    inputRef.current?.click();
  };

  if (displayUrl) {
    return (
      <>
        <div
          className={`relative ${FILLED_MAX_WIDTH[aspectRatio]} rounded-lg overflow-hidden border`}
          onDrop={handleDrop}
          onDragOver={handleDragOver}
          onPaste={handlePaste}
          tabIndex={0}
          aria-label={`${alt}: вставьте или перетащите файл для замены`}
        >
          <div className={`relative ${aspectClass} bg-muted/40`}>
            <ContentImage
              src={displayUrl}
              alt={alt}
              sizes={aspectRatio === "portrait" ? "20rem" : "(min-width: 768px) 42rem, 100vw"}
              className="absolute inset-0 w-full h-full object-cover"
            />
          </div>
          <Button
            type="button"
            variant="secondary"
            size="icon"
            className="absolute top-2 right-2 size-7 bg-white/90 dark:bg-gray-900/90 backdrop-blur-sm shadow-sm hover:bg-white dark:hover:bg-gray-900"
            onClick={handleRemove}
            disabled={isRemoving}
            aria-label="Удалить изображение"
          >
            {isRemoving ? (
              <Icons.loading size={14} className="animate-spin" />
            ) : (
              <Icons.close size={14} />
            )}
          </Button>
        </div>
        <ImageCropperDialog
          imageFile={pendingCrop}
          aspect={ASPECT_NUMBERS[aspectRatio]}
          hint={CROP_HINTS[aspectRatio]}
          onClose={() => setPendingCrop(null)}
          onCropped={(cropped) => upload(cropped)}
        />
      </>
    );
  }

  return (
    <>
      <div
        onDrop={handleDrop}
        onDragOver={handleDragOver}
        onPaste={handlePaste}
        onKeyDown={handleKeyDown}
        role="button"
        tabIndex={0}
        aria-label={`${alt}: выберите, вставьте или перетащите файл`}
        className={`group border-2 border-dashed rounded-lg ${aspectClass} ${FILLED_MAX_WIDTH[aspectRatio]} flex flex-col items-center justify-center text-center cursor-pointer hover:border-muted-foreground/40 transition-colors`}
        onClick={() => inputRef.current?.click()}
      >
        <input
          ref={inputRef}
          type="file"
          accept={ACCEPT}
          className="hidden"
          onChange={handleFileChange}
        />
        {isUploading ? (
          <Icons.loading size={24} className="text-muted-foreground animate-spin" />
        ) : (
          <>
            <div className="size-10 rounded-lg bg-muted/60 flex items-center justify-center mb-3 transition-colors group-hover:bg-muted">
              <Icons.uploadImage size={18} className="text-muted-foreground" />
            </div>
            <p className="text-sm text-muted-foreground transition-colors group-hover:text-foreground">
              Перетащите изображение или нажмите для выбора
            </p>
            <p className="text-xs text-muted-foreground/60 mt-1">
              {CROP_HINTS[aspectRatio]} JPEG, PNG, WebP до 10 МБ
            </p>
          </>
        )}
      </div>
      <ImageCropperDialog
        imageFile={pendingCrop}
        aspect={ASPECT_NUMBERS[aspectRatio]}
        hint={CROP_HINTS[aspectRatio]}
        onClose={() => setPendingCrop(null)}
        onCropped={(cropped) => upload(cropped)}
      />
    </>
  );
}
