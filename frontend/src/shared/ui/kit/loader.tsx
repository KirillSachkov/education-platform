import { cn } from "@/shared/lib/css";
import { Loader2 } from "lucide-react";

type LoaderProps = {
  className?: string;
  size?: "sm" | "md" | "lg";
  text?: string;
};

const sizeClasses = {
  sm: "h-4 w-4",
  md: "h-6 w-6",
  lg: "h-8 w-8",
};

export function Loader({ className, size = "md", text }: LoaderProps) {
  return (
    <div className={cn("flex items-center justify-center gap-2", className)}>
      <Loader2
        className={cn("animate-spin text-muted-foreground", sizeClasses[size])}
      />
      {text && <span className="text-sm text-muted-foreground">{text}</span>}
    </div>
  );
}

type FullPageLoaderProps = {
  text?: string;
};

export function FullPageLoader({ text = "Загрузка..." }: FullPageLoaderProps) {
  return (
    <div className="flex h-full min-h-[200px] w-full items-center justify-center">
      <Loader size="lg" text={text} />
    </div>
  );
}
