"use client";

import { Button } from "@/shared/ui/kit/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { Icons } from "@/shared/ui/icons";
import { useEffect, useRef, useState } from "react";
import Cropper, { type Area } from "react-easy-crop";

interface ImageCropperDialogProps {
  /** When set, the dialog is open and crops this file. */
  imageFile: File | null;
  onClose: () => void;
  onCropped: (croppedFile: File) => void;
  /** Crop aspect ratio: width / height (e.g. 16/9 = 1.777..., 3/4 = 0.75). */
  aspect: number;
  /** Hint text under the cropper (e.g. "Курс — 1280×720"). */
  hint?: string;
  /** Crop overlay shape — "round" for avatars, "rect" (default) for covers. */
  cropShape?: "rect" | "round";
  /** Cap on the output image width in px. Default 1920. */
  maxOutputWidth?: number;
}

const DEFAULT_MAX_OUTPUT_WIDTH = 1920;
const JPEG_QUALITY = 0.92;

export function ImageCropperDialog({
  imageFile,
  onClose,
  onCropped,
  aspect,
  hint,
  cropShape = "rect",
  maxOutputWidth = DEFAULT_MAX_OUTPUT_WIDTH,
}: ImageCropperDialogProps) {
  const [imageSrc, setImageSrc] = useState<string | null>(null);
  const [crop, setCrop] = useState({ x: 0, y: 0 });
  const [zoom, setZoom] = useState(1);
  const [isProcessing, setIsProcessing] = useState(false);
  const croppedAreaRef = useRef<Area | null>(null);

  useEffect(() => {
    if (!imageFile) {
      setImageSrc(null);
      return;
    }
    const url = URL.createObjectURL(imageFile);
    setImageSrc(url);
    setCrop({ x: 0, y: 0 });
    setZoom(1);
    croppedAreaRef.current = null;
    return () => URL.revokeObjectURL(url);
  }, [imageFile]);

  const handleCropComplete = (_: Area, areaPixels: Area) => {
    croppedAreaRef.current = areaPixels;
  };

  const handleConfirm = async () => {
    if (!imageFile || !imageSrc || !croppedAreaRef.current) return;
    try {
      setIsProcessing(true);
      const cropped = await renderCropToFile(
        imageSrc,
        croppedAreaRef.current,
        imageFile.name,
        maxOutputWidth,
      );
      onCropped(cropped);
      onClose();
    } finally {
      setIsProcessing(false);
    }
  };

  const open = imageFile !== null;

  return (
    <Dialog open={open} onOpenChange={(next) => !next && !isProcessing && onClose()}>
      <DialogContent className="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Обрежьте изображение</DialogTitle>
          <DialogDescription>
            {hint ?? "Перетащите и масштабируйте — обрезанная область станет обложкой."}
          </DialogDescription>
        </DialogHeader>

        <div className="relative w-full overflow-hidden rounded-lg bg-black/80 aspect-video">
          {imageSrc && (
            <Cropper
              image={imageSrc}
              crop={crop}
              zoom={zoom}
              aspect={aspect}
              onCropChange={setCrop}
              onZoomChange={setZoom}
              onCropComplete={handleCropComplete}
              objectFit="contain"
              cropShape={cropShape}
              showGrid={cropShape !== "round"}
            />
          )}
        </div>

        <div className="flex items-center gap-3 px-1">
          <Icons.uploadImage size={16} className="text-muted-foreground shrink-0" />
          <input
            type="range"
            min={1}
            max={4}
            step={0.05}
            value={zoom}
            onChange={(e) => setZoom(Number(e.target.value))}
            className="h-1.5 flex-1 cursor-pointer appearance-none rounded-full bg-muted accent-primary"
            aria-label="Масштаб"
          />
          <span className="w-10 text-right text-xs tabular-nums text-muted-foreground">
            {zoom.toFixed(1)}×
          </span>
        </div>

        <DialogFooter>
          <Button type="button" variant="outline" onClick={onClose} disabled={isProcessing}>
            Отмена
          </Button>
          <Button type="button" onClick={handleConfirm} disabled={isProcessing}>
            {isProcessing ? (
              <>
                <Icons.loading size={14} className="animate-spin" />
                Обработка
              </>
            ) : (
              "Применить"
            )}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

async function renderCropToFile(
  imageSrc: string,
  area: Area,
  originalName: string,
  maxOutputWidth: number,
): Promise<File> {
  const image = await loadImage(imageSrc);
  const scale = area.width > maxOutputWidth ? maxOutputWidth / area.width : 1;
  const outWidth = Math.round(area.width * scale);
  const outHeight = Math.round(area.height * scale);

  const canvas = document.createElement("canvas");
  canvas.width = outWidth;
  canvas.height = outHeight;
  const ctx = canvas.getContext("2d");
  if (!ctx) throw new Error("Canvas 2D context unavailable");

  ctx.drawImage(image, area.x, area.y, area.width, area.height, 0, 0, outWidth, outHeight);

  const blob = await new Promise<Blob | null>((resolve) =>
    canvas.toBlob(resolve, "image/jpeg", JPEG_QUALITY),
  );
  if (!blob) throw new Error("Не удалось обработать изображение");

  const fileName = swapExtension(originalName, "jpg");
  return new File([blob], fileName, { type: "image/jpeg", lastModified: Date.now() });
}

function loadImage(src: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.crossOrigin = "anonymous";
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error("Не удалось загрузить изображение"));
    image.src = src;
  });
}

function swapExtension(name: string, newExt: string): string {
  const lastDot = name.lastIndexOf(".");
  const base = lastDot > 0 ? name.slice(0, lastDot) : name;
  return `${base}.${newExt}`;
}
