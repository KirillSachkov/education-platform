import type { Metadata } from "next";
import { PlatformLanding, platformLandingMetadata } from "@/features/course-landing/pages/platform";
import { faq, author } from "@/features/course-landing/pages/platform/config";
import {
  FaqJsonLd,
  BreadcrumbJsonLd,
  ProfilePageJsonLd,
  OrganizationJsonLd,
  WebSiteJsonLd,
} from "@/shared/seo";
import { APP_URL } from "@/shared/config/site";

const ORG_DESCRIPTION =
  "Онлайн-обучение программированию на C#, .NET и ASP.NET Core: программа с AI-ревью каждого PR, " +
  "закрытым Telegram-чатом и помощью с резюме и поиском работы.";

// Social profiles tie the brand to its off-platform presence — a standard
// Organization `sameAs` signal that helps Google/Yandex build the knowledge panel.
const ORG_SAME_AS = [author.socials.youtube, author.socials.telegram, author.socials.github];

export const metadata: Metadata = platformLandingMetadata;

/**
 * Маркетинговый лендинг (`/`). Доступен всем — и анонимам, и залогиненным:
 * авторизованных больше НЕ редиректим на /home, они могут открыть лендинг
 * как обычную страницу (логотип в шапке тоже ведёт сюда).
 *
 * JSON-LD теги корректно сериализуются Next.js на сервере и попадают в HTML
 * до hydration — Google/Yandex их прочитают.
 */
export default function LandingPage() {
  return (
    <>
      <OrganizationJsonLd
        name="SachkovLearn"
        url={APP_URL}
        logo={`${APP_URL}/icons/icon-512.png`}
        description={ORG_DESCRIPTION}
        sameAs={ORG_SAME_AS}
      />
      <WebSiteJsonLd
        name="SachkovLearn"
        url={APP_URL}
        description={ORG_DESCRIPTION}
        publisherName="SachkovLearn"
      />
      <FaqJsonLd items={faq} />
      <BreadcrumbJsonLd
        items={[
          { name: "SachkovLearn", url: APP_URL },
          { name: platformLandingMetadata.title?.toString() ?? "SachkovLearn", url: APP_URL },
        ]}
      />
      <ProfilePageJsonLd
        name={author.name}
        jobTitle={author.role}
        description={author.bio}
        url={APP_URL}
      />
      <PlatformLanding />
    </>
  );
}
