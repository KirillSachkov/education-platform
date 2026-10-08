import { APP_URL } from "@/shared/config/site";
import { BreadcrumbJsonLd, FaqJsonLd } from "@/shared/seo";
import { SeoLandingPage, dotnetLandingContent, seoLandingMetadata } from "@/widgets/seo-landing";

export const metadata = seoLandingMetadata(dotnetLandingContent);

export default function DotnetLandingPage() {
  return (
    <>
      <FaqJsonLd items={dotnetLandingContent.faq} />
      <BreadcrumbJsonLd
        items={[
          { name: "SachkovLearn", url: APP_URL },
          {
            name: dotnetLandingContent.breadcrumbName,
            url: `${APP_URL}${dotnetLandingContent.slug}`,
          },
        ]}
      />
      <SeoLandingPage content={dotnetLandingContent} />
    </>
  );
}
