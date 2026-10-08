import { APP_URL } from "@/shared/config/site";
import { BreadcrumbJsonLd, FaqJsonLd } from "@/shared/seo";
import { SeoLandingPage, csharpLandingContent, seoLandingMetadata } from "@/widgets/seo-landing";

export const metadata = seoLandingMetadata(csharpLandingContent);

export default function CsharpLandingPage() {
  return (
    <>
      <FaqJsonLd items={csharpLandingContent.faq} />
      <BreadcrumbJsonLd
        items={[
          { name: "SachkovLearn", url: APP_URL },
          { name: csharpLandingContent.breadcrumbName, url: `${APP_URL}${csharpLandingContent.slug}` },
        ]}
      />
      <SeoLandingPage content={csharpLandingContent} />
    </>
  );
}
