import type { Metadata } from "next";
import Link from "next/link";
import { LogoMark } from "@/shared/ui/kit/logo";
import { Icons } from "@/shared/ui/icons";
import { SiteFooter } from "@/widgets/site-footer";

export const metadata: Metadata = {
  title: "Юридическая информация",
};

export default function LegalLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-svh bg-background flex flex-col">
      <header className="border-b border-border/60 bg-background/80 backdrop-blur sticky top-0 z-10">
        <div className="mx-auto max-w-3xl px-4 py-3 flex items-center justify-between">
          <Link
            href="/"
            className="flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground transition-colors"
          >
            <Icons.back className="h-4 w-4" />
            На главную
          </Link>
          <Link href="/legal" className="flex items-center gap-2">
            <LogoMark size={20} className="text-primary" />
            <span className="text-sm font-semibold">Юридические документы</span>
          </Link>
        </div>
      </header>

      <main className="mx-auto max-w-3xl px-4 py-8 flex-1 w-full">
        <article className="prose prose-neutral dark:prose-invert max-w-none">
          {children}
        </article>
      </main>

      <SiteFooter />
    </div>
  );
}
