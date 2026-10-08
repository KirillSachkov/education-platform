"use client";

import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/ui/kit/select";

export type DeletedFilterValue = "active" | "all" | "deleted";

type Props = {
  value: DeletedFilterValue;
  onChange: (value: DeletedFilterValue) => void;
};

export function DeletedFilter({ value, onChange }: Props) {
  return (
    <Select value={value} onValueChange={onChange}>
      <SelectTrigger className="w-40">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="active">Активные</SelectItem>
        <SelectItem value="all">Все</SelectItem>
        <SelectItem value="deleted">Удалённые</SelectItem>
      </SelectContent>
    </Select>
  );
}
