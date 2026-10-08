import { cn } from "@/shared/lib/css";

interface LogoMarkProps {
  size?: number;
  className?: string;
  variant?: "dark" | "light";
}

/** Original neutral book illustration; product branding is supplied privately. */
export function LogoMark({ size = 24, className, variant = "dark" }: LogoMarkProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 64 64"
      fill="none"
      className={cn("shrink-0", className)}
      aria-hidden="true"
    >
      <path
        d="M12 14H26L32 20L38 14H52V48H38L32 54L26 48H12Z"
        stroke={variant === "dark" ? "#4dc9b8" : "#007A6C"}
        strokeWidth="4"
        strokeLinejoin="round"
      />
      <path d="M32 20V54" stroke="currentColor" strokeWidth="3" />
    </svg>
  );
}

interface LogoProps {
  size?: number;
  className?: string;
}

export function Logo({ size = 20, className }: LogoProps) {
  return (
    <div className={cn("flex items-center gap-2.5", className)}>
      <LogoMark size={size} />
      <span className="text-sm font-bold tracking-tight leading-none font-[family-name:var(--font-sora)]">
        Education Platform
      </span>
    </div>
  );
}
