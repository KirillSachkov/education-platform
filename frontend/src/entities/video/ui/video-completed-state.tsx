import { CheckCircle2, Video } from "lucide-react";

type Props = {
  fileName?: string;
};

export function VideoCompletedState({ fileName }: Props) {
  return (
    <div className="border border-green-200 bg-green-50 rounded-lg p-4">
      <div className="flex items-center gap-3">
        <div className="w-10 h-10 bg-green-100 rounded-lg flex items-center justify-center">
          <Video className="w-5 h-5 text-green-600" />
        </div>
        <div className="flex-1">
          {fileName && <p className="text-sm font-medium">{fileName}</p>}
          <div className="flex items-center gap-1.5 text-green-600">
            <CheckCircle2 className="w-4 h-4" />
            <span className="text-sm">Видео загружено</span>
          </div>
        </div>
      </div>
    </div>
  );
}
