import Link from "next/link";
import { routes } from "@/shared/config/routes";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";

/**
 * Компактный промо-модуль тренажёра на главной — точка входа в `/trainer`
 * (хаб собес-подготовки: тесты, тренировки, мок-собеседования). Доступ/Pro-гейт
 * разруливает сам хаб, здесь — только приглашающий CTA.
 */
export function TrainerPromoCard() {
  return (
    <Card className="p-5">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-start gap-3">
          <div className="flex size-11 shrink-0 items-center justify-center rounded-xl border border-primary/20 bg-primary/10 text-primary">
            <Icons.energy size={22} />
          </div>
          <div className="min-w-0">
            <p className="text-sm font-semibold">Тренажёр</p>
            <p className="mt-0.5 text-sm text-muted-foreground">
              Проверь и прокачай знания: тесты, тренировки и мок-собеседования
            </p>
          </div>
        </div>

        <Button asChild size="lg" className="min-h-11 w-full shrink-0 sm:w-auto">
          <Link href={routes.trainer}>
            Открыть тренажёр
            <Icons.arrowRight className="size-4" />
          </Link>
        </Button>
      </div>
    </Card>
  );
}
