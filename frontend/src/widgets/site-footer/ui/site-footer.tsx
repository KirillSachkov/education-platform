import Link from "next/link";
import { BusinessDetailsView } from "@/shared/business-details";
import { loadBusinessDetails } from "@/shared/business-details/server";
import { CookieSettingsButton } from "./cookie-settings-button";

export async function SiteFooter() {
  const details = await loadBusinessDetails();

  return (
    <footer className="border-t border-border/60 bg-card/30 mt-auto">
      <div className="mx-auto max-w-6xl px-4 py-8 text-xs text-muted-foreground">
        <div className="grid gap-6 md:grid-cols-3">
          <div>
            <div className="font-semibold text-foreground mb-2">SachkovLearn</div>
            <p>
              Информационно-консультационная платформа по программированию: C#, .NET, AI, DevOps,
              микросервисы.
            </p>
          </div>

          <div>
            <div className="font-semibold text-foreground mb-2">Документы</div>
            <ul className="space-y-1">
              <li>
                <Link href="/legal/offer" className="hover:text-foreground transition-colors">
                  Договор-оферта
                </Link>
              </li>
              <li>
                <Link href="/legal/privacy" className="hover:text-foreground transition-colors">
                  Политика обработки ПДн
                </Link>
              </li>
              <li>
                <Link href="/legal/consent-pd" className="hover:text-foreground transition-colors">
                  Согласие на обработку ПДн
                </Link>
              </li>
              <li>
                <Link href="/legal/cookies" className="hover:text-foreground transition-colors">
                  Политика cookies
                </Link>
              </li>
              <li>
                <Link
                  href="/legal/consent-marketing"
                  className="hover:text-foreground transition-colors"
                >
                  Согласие на рассылку
                </Link>
              </li>
            </ul>
          </div>

          {details ? (
            <div>
              <div className="font-semibold text-foreground mb-2">Реквизиты</div>
              <address className="not-italic space-y-0.5">
                <BusinessDetailsView details={details} />
              </address>
            </div>
          ) : null}
        </div>

        <div className="mt-6 pt-4 border-t border-border/40 flex flex-wrap items-center justify-between gap-2">
          <div>
            © {new Date().getFullYear()} {details?.copyrightName ?? "SachkovLearn"} Все права
            защищены.
          </div>
          <div className="flex items-center gap-4">
            <CookieSettingsButton />
            <span className="text-muted-foreground/70">
              Услуги не являются образовательной деятельностью по 273-ФЗ.
            </span>
          </div>
        </div>
      </div>
    </footer>
  );
}
