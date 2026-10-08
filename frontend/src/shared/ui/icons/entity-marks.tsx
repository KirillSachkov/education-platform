import { forwardRef, type SVGProps, type ReactNode } from "react";
import { cn } from "@/shared/lib/css";

/**
 * Custom entity marks for the platform's domain objects.
 *
 * Designed as a cohesive set with a single visual language — rounded strokes,
 * consistent weight, 24×24 viewBox — so the platform avoids the generic
 * lucide/feather look and has a distinctive editorial identity.
 *
 * API mirrors lucide-react: accepts `size`, `className`, `strokeWidth`, and
 * spreads the rest onto the underlying <svg>. Drop-in replacement anywhere
 * a lucide icon component is expected.
 */

export interface EntityMarkProps extends Omit<SVGProps<SVGSVGElement>, "ref"> {
  size?: number | string;
}

function createMark(displayName: string, children: ReactNode) {
  const Mark = forwardRef<SVGSVGElement, EntityMarkProps>(
    ({ size = 24, className, strokeWidth = 1.75, ...rest }, ref) => (
      <svg
        ref={ref}
        xmlns="http://www.w3.org/2000/svg"
        width={size}
        height={size}
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth={strokeWidth}
        strokeLinecap="round"
        strokeLinejoin="round"
        className={cn("shrink-0", className)}
        {...rest}
      >
        {children}
      </svg>
    ),
  );
  Mark.displayName = displayName;
  return Mark;
}

/**
 * Module — three stacked rounded bars of varying length.
 * Evokes "structured content outline" / chapters of a syllabus.
 */
export const ModuleMark = createMark(
  "ModuleMark",
  <>
    <rect x="3" y="4" width="18" height="3.5" rx="1.25" />
    <rect x="3" y="10.25" width="12" height="3.5" rx="1.25" />
    <rect x="3" y="16.5" width="15" height="3.5" rx="1.25" />
  </>,
);

/**
 * Lesson — refined open-book silhouette with a central spine and
 * subtle reading-line accent (one short mark inside a page).
 */
export const LessonMark = createMark(
  "LessonMark",
  <>
    <path d="M12 6.5c-2.4-1-5.8-1.5-9-1.5v13c3.2 0 6.6.5 9 1.5" />
    <path d="M12 6.5c2.4-1 5.8-1.5 9-1.5v13c-3.2 0-6.6.5-9 1.5" />
    <path d="M12 6.5V19" />
    <path d="M6 10h3" />
  </>,
);

/**
 * Issue — angle brackets enclosing a pulse dot.
 * Clearly "code task" without duplicating lucide's Code2 glyph.
 */
export const IssueMark = createMark(
  "IssueMark",
  <>
    <path d="M9 6 3 12l6 6" />
    <path d="m15 6 6 6-6 6" />
    <circle cx="12" cy="12" r="1.25" fill="currentColor" stroke="none" />
  </>,
);

/**
 * Project — two offset rounded rectangles, one in front, one behind.
 * Reads as "layered deliverables" / nested scope without being a folder.
 */
export const ProjectMark = createMark(
  "ProjectMark",
  <>
    <rect x="3" y="8" width="13" height="13" rx="2" />
    <path d="M8 8V3h13v13h-5" />
  </>,
);

/**
 * Course — abstract bookmark / milestone banner.
 * Distinct from lucide's GraduationCap — softer, editorial feel.
 */
export const CourseMark = createMark(
  "CourseMark",
  <>
    <path d="M6 3h12a1 1 0 0 1 1 1v16.3a.7.7 0 0 1-1.1.6L12 17.2 6.1 20.9A.7.7 0 0 1 5 20.3V4a1 1 0 0 1 1-1Z" />
    <path d="M9 8h6" />
    <path d="M9 12h4" />
  </>,
);

/**
 * Article — document with a folded corner and a reading line.
 */
export const ArticleMark = createMark(
  "ArticleMark",
  <>
    <path d="M14 3H6a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V8Z" />
    <path d="M14 3v5h5" />
    <path d="M9 13h6" />
    <path d="M9 17h4" />
  </>,
);
