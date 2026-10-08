"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { scrollFadeMask, useAdminTimeRange, useScrollAffordance } from "@/shared/hooks";
import { DateRangePicker } from "@/shared/ui/kit/date-range-picker";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/shared/ui/kit/tabs";
import { AccessTab } from "./tabs/access-tab";
import { LearningTab } from "./tabs/learning-tab";
import { OverviewTab } from "./tabs/overview-tab";
import { UsersTab } from "./tabs/users-tab";

const TAB_PARAM = "tab";
const VALID_TABS = new Set(["overview", "users", "access", "learning"]);
const DEFAULT_TAB = "overview";

function isFutureDate(date: Date): boolean {
  return date.getTime() > Date.now();
}

export function AdminOverviewPage() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const { range, presetId, setPreset, setCustom } = useAdminTimeRange();

  const tabFromUrl = searchParams.get(TAB_PARAM);
  const tab = tabFromUrl && VALID_TABS.has(tabFromUrl) ? tabFromUrl : DEFAULT_TAB;
  const { ref, atStart, atEnd } = useScrollAffordance<HTMLDivElement>(tab);

  const handleTabChange = (next: string) => {
    const params = new URLSearchParams(searchParams.toString());
    if (next === DEFAULT_TAB) {
      params.delete(TAB_PARAM);
    } else {
      params.set(TAB_PARAM, next);
    }
    const qs = params.toString();
    router.replace(qs ? `${pathname}?${qs}` : pathname, { scroll: false });
  };

  return (
    <div className="container mx-auto max-w-6xl space-y-6 p-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold">Обзор</h1>
          <p className="text-sm text-muted-foreground">
            KPI и активность платформы. Данные обновляются каждые 30 секунд.
          </p>
        </div>
        <DateRangePicker
          value={range}
          onChange={setCustom}
          onPresetSelect={setPreset}
          presetId={presetId}
          disabledDates={isFutureDate}
        />
      </div>

      <Tabs value={tab} onValueChange={handleTabChange} className="space-y-6">
        <TabsList
          ref={ref}
          style={scrollFadeMask(atStart, atEnd)}
          className="w-full justify-start overflow-x-auto scrollbar-none md:w-fit"
        >
          <TabsTrigger value="overview">Общее</TabsTrigger>
          <TabsTrigger value="users">Пользователи</TabsTrigger>
          <TabsTrigger value="access">Доступ и платежи</TabsTrigger>
          <TabsTrigger value="learning">Обучение</TabsTrigger>
        </TabsList>

        <TabsContent value="overview" className="mt-0">
          <OverviewTab range={range} />
        </TabsContent>
        <TabsContent value="users" className="mt-0">
          <UsersTab range={range} />
        </TabsContent>
        <TabsContent value="access" className="mt-0">
          <AccessTab range={range} />
        </TabsContent>
        <TabsContent value="learning" className="mt-0">
          <LearningTab range={range} />
        </TabsContent>
      </Tabs>
    </div>
  );
}
