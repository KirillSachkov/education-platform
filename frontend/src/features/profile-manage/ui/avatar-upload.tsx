"use client";

import { API_ORIGIN } from "@/shared/api";
import { Avatar, AvatarFallback, AvatarImage } from "@/shared/ui/kit/avatar";
import { ImageCropperDialog } from "@/shared/ui/components/image-cropper-dialog";
import { Button } from "@/shared/ui/kit/button";
import { Camera, Loader2, X } from "lucide-react";
import { useRef, useState } from "react";
import { useUploadAvatar } from "../model/use-upload-avatar";

const ACCEPT = "image/jpeg,image/png,image/webp";

type Props = {
  userId: string;
  avatarId: string | null;
  name: string;
};

export function AvatarUpload({ userId, avatarId, name }: Props) {
  const { upload, isUploading, avatarUrl, uploadedAssetId, isDeleted, remove, isRemoving } =
    useUploadAvatar(userId);

  const inputRef = useRef<HTMLInputElement>(null);
  const [pendingCrop, setPendingCrop] = useState<File | null>(null);

  const initials = name
    .split(" ")
    .map((w) => w[0])
    .join("")
    .toUpperCase()
    .slice(0, 2);

  const resolveUrl = (url: string | null | undefined) =>
    url && url.startsWith("/") ? `${API_ORIGIN}${url}` : (url ?? null);

  const currentAvatarUrl =
    resolveUrl(avatarUrl) ??
    (isDeleted ? null : avatarId ? `${API_ORIGIN}/files/${avatarId}/content` : null);

  const currentImageId = uploadedAssetId ?? avatarId;

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      setPendingCrop(file);
    }
    if (inputRef.current) {
      inputRef.current.value = "";
    }
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    const file = e.dataTransfer.files[0];
    if (file) {
      setPendingCrop(file);
    }
  };

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
  };

  return (
    <>
      <div className="relative shrink-0">
        <div
          className="group relative cursor-pointer"
          onClick={() => inputRef.current?.click()}
          onDrop={handleDrop}
          onDragOver={handleDragOver}
        >
          <Avatar className="size-24 ring-2 ring-border shadow-lg">
            {currentAvatarUrl && <AvatarImage src={currentAvatarUrl} alt={name} />}
            <AvatarFallback className="text-xl font-semibold bg-gradient-primary text-primary-foreground">
              {initials}
            </AvatarFallback>
          </Avatar>

          {/* Hover overlay */}
          <div className="absolute inset-0 rounded-full bg-black/40 flex items-center justify-center opacity-0 group-hover:opacity-100 transition-opacity">
            {isUploading ? (
              <Loader2 size={20} className="text-white animate-spin" />
            ) : (
              <Camera size={20} className="text-white" />
            )}
          </div>

          <input
            ref={inputRef}
            type="file"
            accept={ACCEPT}
            className="hidden"
            onChange={handleFileChange}
          />
        </div>

        {/* Delete button */}
        {currentImageId && currentAvatarUrl && (
          <Button
            type="button"
            variant="secondary"
            size="icon"
            className="absolute -top-1 -right-1 size-6 rounded-full bg-white/90 dark:bg-gray-900/90 backdrop-blur-sm shadow-sm hover:bg-white dark:hover:bg-gray-900"
            onClick={(e) => {
              e.stopPropagation();
              remove(currentImageId);
            }}
            disabled={isRemoving}
          >
            {isRemoving ? <Loader2 size={12} className="animate-spin" /> : <X size={12} />}
          </Button>
        )}
      </div>
      <ImageCropperDialog
        imageFile={pendingCrop}
        aspect={1}
        cropShape="round"
        maxOutputWidth={512}
        hint="Аватар — квадрат, рекомендуем 512×512 и крупнее."
        onClose={() => setPendingCrop(null)}
        onCropped={(cropped) => upload(cropped)}
      />
    </>
  );
}
