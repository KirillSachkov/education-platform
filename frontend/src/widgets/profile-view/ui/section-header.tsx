import { Button } from "@/shared/ui/kit/button";
import { Check, Edit2, Loader2, X } from "lucide-react";

export function SectionHeader({
  editing,
  onEdit,
  onSave,
  onCancel,
  isPending,
}: {
  editing: boolean;
  onEdit: () => void;
  onSave: () => void;
  onCancel: () => void;
  isPending: boolean;
}) {
  if (editing) {
    return (
      <div className="flex items-center gap-2">
        <Button size="sm" onClick={onSave} disabled={isPending}>
          {isPending ? (
            <Loader2 size={14} className="mr-1.5 animate-spin" />
          ) : (
            <Check size={14} className="mr-1.5" />
          )}
          {isPending ? "Сохранение..." : "Сохранить"}
        </Button>
        <Button
          size="sm"
          variant="ghost"
          onClick={onCancel}
          disabled={isPending}
        >
          <X size={14} className="mr-1" />
          Отмена
        </Button>
      </div>
    );
  }

  return (
    <Button variant="ghost" size="sm" onClick={onEdit}>
      <Edit2 size={14} className="mr-1.5" />
      Редактировать
    </Button>
  );
}

export function EmptyProfileState({
  roleName,
  onEdit,
}: {
  roleName: string;
  onEdit: () => void;
}) {
  return (
    <div className="flex flex-col items-center justify-center py-12 text-center">
      <div className="size-12 rounded-full bg-muted flex items-center justify-center mb-4">
        <Edit2 size={20} className="text-muted-foreground" />
      </div>
      <p className="text-sm text-muted-foreground mb-1">
        Профиль {roleName} не заполнен
      </p>
      <p className="text-xs text-muted-foreground/60 mb-4">
        Заполните информацию, чтобы другие пользователи могли узнать о вас
        больше
      </p>
      <Button variant="outline" size="sm" onClick={onEdit}>
        <Edit2 size={14} className="mr-1.5" />
        Заполнить
      </Button>
    </div>
  );
}
