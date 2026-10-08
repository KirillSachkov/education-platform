/** Активность за один календарный день (UTC). */
export interface ActivityDayDto {
  /** Дата дня в формате "yyyy-MM-dd" (UTC). */
  date: string;
  /** Сумма начисленного XP за день. */
  xp: number;
  /** Количество материалов, отмеченных изученными в этот день. */
  materialsCompleted: number;
}

/** Стрик — серии последовательных дней с любой активностью. */
export interface ActivityStreakDto {
  /** Текущая серия: дни подряд, заканчивающиеся сегодня или вчера. */
  current: number;
  /** Лучшая серия за всю историю. */
  longest: number;
}

/** Суммарные итоги пользователя за всё время. */
export interface ActivityTotalsDto {
  totalXp: number;
  materialsCompleted: number;
  issuesApproved: number;
}

/** Ответ GET /progress/my/activity/ — данные страницы «Мой прогресс». */
export interface MyActivityDto {
  /** Ровно 84 дня (12 недель), включая сегодня, от старых к новым. */
  days: ActivityDayDto[];
  streak: ActivityStreakDto;
  totals: ActivityTotalsDto;
}
