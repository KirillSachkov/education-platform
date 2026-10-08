/**
 * Лёгкий стрик-каунтер тренажёра (#568 Ф2): сколько дней подряд (включая
 * сегодня/вчера) была активность. Источник — таймстемпы сессий (`get-my-sessions`).
 * Без бэкенда: чисто клиентский вывод из истории. Не импортирует React.
 *
 * Логика: берём множество дат-дней активности (локальная зона), идём от
 * сегодня назад, считаем непрерывную цепочку. Если активности нет ни сегодня, ни
 * вчера — стрик 0 (цепочка прервана). Допускаем «вчера» как стартовую точку,
 * чтобы стрик не обнулялся до конца текущего дня.
 */
export function computeActivityStreak(timestamps: ReadonlyArray<string | null>): number {
  const days = new Set<string>();
  for (const ts of timestamps) {
    if (!ts) continue;
    const date = new Date(ts);
    if (Number.isNaN(date.getTime())) continue;
    days.add(toDayKey(date));
  }
  if (days.size === 0) return 0;

  const today = new Date();
  const todayKey = toDayKey(today);
  const yesterdayKey = toDayKey(addDays(today, -1));

  // Цепочка должна доходить до сегодня или вчера — иначе она прервана.
  let cursor: Date;
  if (days.has(todayKey)) cursor = today;
  else if (days.has(yesterdayKey)) cursor = addDays(today, -1);
  else return 0;

  let streak = 0;
  while (days.has(toDayKey(cursor))) {
    streak += 1;
    cursor = addDays(cursor, -1);
  }
  return streak;
}

/** Ключ дня в локальной зоне — `YYYY-MM-DD`. */
function toDayKey(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function addDays(date: Date, delta: number): Date {
  const next = new Date(date);
  next.setDate(next.getDate() + delta);
  return next;
}

/** Грамматика «день/дня/дней» для подписи стрика. */
export function pluralizeDays(count: number): string {
  const mod10 = count % 10;
  const mod100 = count % 100;
  if (mod10 === 1 && mod100 !== 11) return "день";
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20)) return "дня";
  return "дней";
}
