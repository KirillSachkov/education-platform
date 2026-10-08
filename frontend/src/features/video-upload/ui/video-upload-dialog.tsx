"use client";

import {
  getVideoAcceptString,
  VIDEO_CONFIG,
  VideoCompletedState,
  VideoDropZone,
  VideoErrorState,
  VideoUploadingState,
  type UploadProgress,
} from "@/entities/video";
import { getFirstFile, getFirstFileFromClipboard } from "@/shared/lib/file-transfer";
import { Button } from "@/shared/ui/kit/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/shared/ui/kit/tabs";
import { Loader2 } from "lucide-react";
import { useRef, useState } from "react";

export type VideoUploadDialogProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title?: string;
  description?: string;
  uploadState: UploadProgress;
  onUpload: (file: File) => Promise<string | undefined>;
  onCancel: () => Promise<void>;
  onReset: () => void;
  onAttachExternal?: (externalVideoId: string) => Promise<unknown>;
  isAttaching?: boolean;
  attachError?: string | null;
};

export function VideoUploadDialog({
  open,
  onOpenChange,
  title = "Загрузка видео",
  description = VIDEO_CONFIG.description,
  uploadState,
  onUpload,
  onCancel,
  onReset,
  onAttachExternal,
  isAttaching = false,
  attachError,
}: VideoUploadDialogProps) {
  const [isDragging, setIsDragging] = useState(false);
  const [externalVideoId, setExternalVideoId] = useState("");
  const [activeTab, setActiveTab] = useState<string>("upload");
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(true);
  };

  const handleDragLeave = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(false);
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(false);
    const file = getFirstFile(e.dataTransfer.files);
    if (file) handleFileSelected(file);
  };

  const handlePaste = (e: React.ClipboardEvent) => {
    const file = getFirstFileFromClipboard(e.clipboardData);
    if (!file) return;
    e.preventDefault();
    void handleFileSelected(file);
  };

  const handleFileInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = getFirstFile(e.target.files);
    if (file) handleFileSelected(file);
    if (fileInputRef.current) {
      fileInputRef.current.value = "";
    }
  };

  const openFilePicker = () => fileInputRef.current?.click();

  const handleFileSelected = async (file: File) => {
    const videoId = await onUpload(file);
    if (videoId) {
      onOpenChange(false);
      setTimeout(() => onReset(), 300);
    }
  };

  const handleCancel = async () => {
    await onCancel();
  };

  const handleRetry = () => {
    onReset();
    openFilePicker();
  };

  const handleAttachExternal = async () => {
    if (!externalVideoId.trim() || !onAttachExternal) return;
    try {
      await onAttachExternal(externalVideoId.trim());
      setExternalVideoId("");
      onOpenChange(false);
    } catch {}
  };

  const handleClose = (nextOpen: boolean) => {
    if (!nextOpen && uploadState.status === "idle" && !isAttaching) {
      onOpenChange(false);
      setExternalVideoId("");
      setActiveTab("upload");
    } else if (!nextOpen && uploadState.status === "completed") {
      onOpenChange(false);
      setTimeout(() => onReset(), 300);
    }
  };

  const showTabs = !!onAttachExternal;

  return (
    <Dialog open={open} onOpenChange={handleClose}>
      <DialogContent className="min-w-0 sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>

        <input
          ref={fileInputRef}
          type="file"
          accept={getVideoAcceptString()}
          className="hidden"
          onChange={handleFileInputChange}
          aria-label="Выбор видео"
        />

        {showTabs ? (
          <Tabs value={activeTab} onValueChange={setActiveTab} className="min-w-0">
            <TabsList className="grid w-full grid-cols-2">
              <TabsTrigger value="upload">Загрузить</TabsTrigger>
              <TabsTrigger value="attach">Прикрепить</TabsTrigger>
            </TabsList>

            <TabsContent value="upload" className="min-w-0 space-y-4">
              {uploadState.status === "idle" && (
                <VideoDropZone
                  isDragging={isDragging}
                  onDragOver={handleDragOver}
                  onDragLeave={handleDragLeave}
                  onDrop={handleDrop}
                  onPaste={handlePaste}
                  onClick={openFilePicker}
                />
              )}

              {uploadState.status === "uploading" && (
                <VideoUploadingState
                  fileName={uploadState.fileName}
                  fileSize={uploadState.fileSize}
                  progress={uploadState.progress}
                  uploadedBytes={uploadState.uploadedBytes}
                  totalBytes={uploadState.totalBytes}
                  onCancel={handleCancel}
                />
              )}

              {uploadState.status === "completed" && (
                <VideoCompletedState fileName={uploadState.fileName} />
              )}

              {uploadState.status === "failed" && (
                <VideoErrorState error={uploadState.error} onRetry={handleRetry} />
              )}
            </TabsContent>

            <TabsContent value="attach" className="min-w-0 space-y-4">
              <div className="space-y-2">
                <Label htmlFor="externalVideoId">Kinescope Video ID</Label>
                <Input
                  id="externalVideoId"
                  placeholder="Например: abc123xyz"
                  value={externalVideoId}
                  onChange={(e) => setExternalVideoId(e.target.value)}
                  disabled={isAttaching}
                />
                <p className="text-xs text-muted-foreground">
                  ID видео из Kinescope. Можно найти в URL видео или в панели Kinescope.
                </p>
              </div>

              {attachError && <p className="text-sm text-destructive">{attachError}</p>}

              <Button
                onClick={handleAttachExternal}
                disabled={!externalVideoId.trim() || isAttaching}
                className="w-full"
              >
                {isAttaching ? (
                  <>
                    <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                    Прикрепление...
                  </>
                ) : (
                  "Прикрепить видео"
                )}
              </Button>
            </TabsContent>
          </Tabs>
        ) : (
          <div className="min-w-0 space-y-4">
            {uploadState.status === "idle" && (
              <VideoDropZone
                isDragging={isDragging}
                onDragOver={handleDragOver}
                onDragLeave={handleDragLeave}
                onDrop={handleDrop}
                onPaste={handlePaste}
                onClick={openFilePicker}
              />
            )}

            {uploadState.status === "uploading" && (
              <VideoUploadingState
                fileName={uploadState.fileName}
                fileSize={uploadState.fileSize}
                progress={uploadState.progress}
                uploadedBytes={uploadState.uploadedBytes}
                totalBytes={uploadState.totalBytes}
                onCancel={handleCancel}
              />
            )}

            {uploadState.status === "completed" && (
              <VideoCompletedState fileName={uploadState.fileName} />
            )}

            {uploadState.status === "failed" && (
              <VideoErrorState error={uploadState.error} onRetry={handleRetry} />
            )}
          </div>
        )}

        <DialogFooter>
          {uploadState.status === "idle" && activeTab === "upload" && (
            <Button variant="outline" onClick={() => onOpenChange(false)}>
              Отмена
            </Button>
          )}
          {uploadState.status === "completed" && (
            <Button onClick={() => onOpenChange(false)}>Готово</Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
