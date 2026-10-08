import Link from "next/link";
import { Button } from "@/shared/ui/kit/button";

export default function NotFound() {
  return (
    <div className="flex flex-col items-center justify-center min-h-[60vh] gap-4">
      <h2 className="text-2xl font-semibold">Курс не найден</h2>
      <p className="text-muted-foreground">
        Запрашиваемый курс не существует или был удалён
      </p>
      <Button asChild variant="outline">
        <Link href="/">На главную</Link>
      </Button>
    </div>
  );
}
