"use client";

import { resolveImageUrl } from "@/shared/lib/image-src";
import { getFirstFile, getFirstFileFromClipboard } from "@/shared/lib/file-transfer";
import { ContentImage } from "@/shared/ui/components/content-image";
import { Button } from "@/shared/ui/kit/button";
import { ImagePlus, Loader2, X } from "lucide-react";
import { useRef } from "react";
import type { MaterialPreviewUploadResult } from "../model/use-upload-material-preview";

const ACCEPT = "image/jpeg,image/png,image/webp";

type Props = {
  hook: MaterialPreviewUploadResult;
  imageId?: string | null;
  initialPreviewUrl?: string | null;
};

export function MaterialPreviewUpload({ hook, imageId, initialPreviewUrl }: Props) {
  const { upload, isUploading, previewUrl, uploadedAssetId, isDeleted, remove, isRemoving } = hook;

  const displayUrl =
    resolveImageUrl(previewUrl) ?? (isDeleted ? null : resolveImageUrl(initialPreviewUrl));
  const inputRef = useRef<HTMLInputElement>(null);

  const currentImageId = uploadedAssetId ?? imageId;

  const handleRemove = () => {
    if (currentImageId) {
      remove(currentImageId);
    }
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = getFirstFile(e.target.files);
    if (file) {
      upload(file);
    }
    if (inputRef.current) {
      inputRef.current.value = "";
    }
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    const file = getFirstFile(e.dataTransfer.files);
    if (file) {
      upload(file);
    }
  };

  const handlePaste = (e: React.ClipboardEvent) => {
    const file = getFirstFileFromClipboard(e.clipboardData);
    if (file) {
      e.preventDefault();
      upload(file);
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
      <div
        className="relative max-w-2xl rounded-lg overflow-hidden border"
        onDrop={handleDrop}
        onDragOver={handleDragOver}
        onPaste={handlePaste}
        tabIndex={0}
        aria-label="Обложка материала: вставьте или перетащите файл для замены"
      >
        <div className="relative aspect-video bg-muted/40">
          <ContentImage
            src={displayUrl}
            alt="Обложка материала"
            sizes="(min-width: 768px) 42rem, 100vw"
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
          {isRemoving ? <Loader2 size={14} className="animate-spin" /> : <X size={14} />}
        </Button>
      </div>
    );
  }

  return (
    <div
      onDrop={handleDrop}
      onDragOver={handleDragOver}
      onPaste={handlePaste}
      onKeyDown={handleKeyDown}
      role="button"
      tabIndex={0}
      aria-label="Обложка материала: выберите, вставьте или перетащите файл"
      className="group border-2 border-dashed rounded-lg aspect-video max-w-2xl flex flex-col items-center justify-center text-center cursor-pointer hover:border-muted-foreground/40 transition-colors"
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
        <Loader2 size={24} className="text-muted-foreground animate-spin" />
      ) : (
        <>
          <div className="size-10 rounded-lg bg-muted/60 flex items-center justify-center mb-3 transition-colors group-hover:bg-muted">
            <ImagePlus size={18} className="text-muted-foreground" />
          </div>
          <p className="text-sm text-muted-foreground transition-colors group-hover:text-foreground">
            Перетащите изображение или нажмите для выбора
          </p>
          <p className="text-xs text-muted-foreground/60 mt-1">JPEG, PNG, WebP до 10 МБ</p>
        </>
      )}
    </div>
  );
}
