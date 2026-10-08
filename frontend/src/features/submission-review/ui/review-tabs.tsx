"use client";

import { cn } from "@/shared/lib/css";

export type ReviewTab = "pending" | "in_review" | "reviewed";

const tabs: { id: ReviewTab; label: string }[] = [
  { id: "pending", label: "Ожидают проверки" },
  { id: "in_review", label: "В проверке" },
  { id: "reviewed", label: "Проверенные" },
];

interface ReviewTabsProps {
  activeTab: ReviewTab;
  onChange: (tab: ReviewTab) => void;
}

export function ReviewTabs({ activeTab, onChange }: ReviewTabsProps) {
  return (
    <div className="flex items-center gap-1 bg-muted/50 rounded-xl p-1 w-fit max-w-full overflow-x-auto">
      {tabs.map((tab) => (
        <button
          key={tab.id}
          onClick={() => onChange(tab.id)}
          className={cn(
            "shrink-0 whitespace-nowrap px-3 py-1.5 rounded-lg text-sm font-medium transition-colors",
            activeTab === tab.id
              ? "bg-background text-foreground shadow-sm"
              : "text-muted-foreground hover:text-foreground",
          )}
        >
          {tab.label}
        </button>
      ))}
    </div>
  );
}
