import Link from "next/link";
import { ArrowLeft, BookOpen } from "lucide-react";
import { Logo } from "@/shared/ui/kit/logo";

export default function NotFound() {
  return (
    <div className="min-h-svh bg-background text-foreground flex flex-col relative overflow-hidden">
      {/* Background decoration */}
      <div className="pointer-events-none absolute inset-0">
        <div className="absolute top-[-20%] right-[-10%] w-[600px] h-[600px] rounded-full bg-primary/[0.04] blur-3xl" />
        <div className="absolute bottom-[-15%] left-[-5%] w-[500px] h-[500px] rounded-full bg-cyan/[0.03] blur-3xl" />
      </div>

      {/* Header */}
      <header className="relative border-b border-border/40 bg-background/60 backdrop-blur-xl">
        <div className="mx-auto flex h-16 max-w-5xl items-center justify-between px-6">
          <Link href="/">
            <Logo size={22} />
          </Link>
          <Link
            href="/"
            className="text-sm font-medium text-muted-foreground hover:text-foreground transition-colors"
          >
            На главную
          </Link>
        </div>
      </header>

      {/* Content */}
      <main className="relative flex-1 flex items-center justify-center px-6 py-20">
        <div className="max-w-lg w-full text-center space-y-8">
          {/* 404 number */}
          <div className="relative inline-block">
            <span className="font-[family-name:var(--font-jetbrains-mono)] text-[8rem] sm:text-[10rem] font-extrabold leading-none tracking-tighter bg-gradient-to-br from-primary via-cyan to-primary bg-clip-text text-transparent select-none">
              404
            </span>
          </div>

          {/* Description */}
          <p className="text-muted-foreground text-base leading-relaxed">
            Страница не найдена. Возможно, она была удалена или перемещена.
          </p>

          {/* Actions */}
          <div className="flex flex-wrap items-center justify-center gap-4">
            <Link
              href="/"
              className="inline-flex items-center gap-2 rounded-xl border border-border px-6 py-3 text-sm font-semibold text-foreground hover:bg-secondary transition-colors"
            >
              <ArrowLeft className="size-4" />
              На главную
            </Link>
            <Link
              href="/"
              className="inline-flex items-center gap-2 rounded-xl bg-gradient-primary px-6 py-3 text-sm font-bold text-primary-foreground shadow-lg shadow-primary/20 hover:shadow-xl hover:shadow-primary/25 hover:-translate-y-0.5 transition-all"
            >
              <BookOpen className="size-4" />
              На главную
            </Link>
          </div>
        </div>
      </main>

      {/* Footer */}
      <footer className="relative border-t border-border/40 py-6">
        <div className="mx-auto max-w-5xl px-6 flex items-center justify-between text-xs text-muted-foreground">
          <span suppressHydrationWarning>
            SachkovLearn {new Date().getFullYear()}
          </span>
          <Link
            href="/"
            className="hover:text-foreground transition-colors"
          >
            На главную
          </Link>
        </div>
      </footer>
    </div>
  );
}
