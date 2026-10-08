import { LandingHeader } from "@/features/landing-header";
import { routes } from "@/shared/config/routes";

const ANCHOR_LINKS = [
  { label: "Программа", sectionId: "program" },
  { label: "Обучение", sectionId: "learning" },
  { label: "Отзывы", sectionId: "testimonials" },
  { label: "Автор", sectionId: "author" },
  { label: "Планы", sectionId: "price" },
];

export default function LandingLayout({ children }: { children: React.ReactNode }) {
  // Marketing landing is a deliberately dark, hand-crafted hero (hardcoded
  // #0A0A0B surfaces). Force the dark token scope + color-scheme so it renders
  // consistently even when the visitor's app theme is light — and so any
  // semantic-token component added here later can't leak light surfaces in.
  return (
    <div className="dark [color-scheme:dark] min-h-svh flex flex-col bg-[#0A0A0B]">
      <LandingHeader
        anchorLinks={ANCHOR_LINKS}
        ctaText="Перейти на платформу"
        ctaHref={routes.home}
      />
      {/* -mt-14 pulls content under the sticky LandingHeader (h-14), preserving
          the transparent-overlay hero while fixing the iOS Safari scroll gap. */}
      <main id="main-content" className="-mt-14 flex-1">
        {children}
      </main>
    </div>
  );
}
