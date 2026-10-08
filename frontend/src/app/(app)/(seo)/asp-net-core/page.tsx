import { APP_URL } from "@/shared/config/site";
import { BreadcrumbJsonLd, FaqJsonLd } from "@/shared/seo";
import {
  SeoLandingPage,
  aspNetCoreLandingContent,
  seoLandingMetadata,
} from "@/widgets/seo-landing";

export const metadata = seoLandingMetadata(aspNetCoreLandingContent);

export default function AspNetCoreLandingPage() {
  return (
    <>
      <FaqJsonLd items={aspNetCoreLandingContent.faq} />
      <BreadcrumbJsonLd
        items={[
          { name: "SachkovLearn", url: APP_URL },
          {
            name: aspNetCoreLandingContent.breadcrumbName,
            url: `${APP_URL}${aspNetCoreLandingContent.slug}`,
          },
        ]}
      />
      <SeoLandingPage content={aspNetCoreLandingContent} />
    </>
  );
}
