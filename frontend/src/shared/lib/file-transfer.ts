export function getFirstFile(files: FileList | File[] | null | undefined): File | null {
  if (!files || files.length === 0) return null;
  return files[0] ?? null;
}

export function getFirstFileFromClipboard(clipboardData: DataTransfer | null): File | null {
  const file = getFirstFile(clipboardData?.files);
  if (file) return file;

  for (const item of Array.from(clipboardData?.items ?? [])) {
    if (item.kind !== "file") continue;
    const itemFile = item.getAsFile();
    if (itemFile) return itemFile;
  }

  return null;
}
