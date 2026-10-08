type Props = {
  totalCount: number;
};

export function LeaderboardHeader({ totalCount }: Props) {
  return (
    <section className="relative overflow-hidden rounded-3xl border border-border/60 bg-gradient-to-br from-primary/10 via-card to-gold/8 p-6 sm:p-8">
      <div className="absolute inset-y-0 right-0 w-1/3 bg-[radial-gradient(circle_at_center,_oklch(0.78_0.08_85_/_0.15),_transparent_70%)]" />

      <div className="relative space-y-3">
        <div className="space-y-2">
          <h1 className="text-2xl font-bold tracking-tight sm:text-3xl">
            Топ пользователей
          </h1>
          <p className="max-w-2xl text-sm text-muted-foreground sm:text-base">
            Рейтинг формируется по суммарному количеству накопленного опыта.
            Чем больше XP, тем выше ваше место в таблице.
          </p>
        </div>

        <p className="text-sm text-muted-foreground">
          В рейтинге сейчас{" "}
          <span className="font-semibold text-foreground">
            {totalCount.toLocaleString()}
          </span>{" "}
          участников
        </p>
      </div>
    </section>
  );
}
