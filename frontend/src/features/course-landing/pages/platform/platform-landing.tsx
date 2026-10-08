import { loadBusinessDetails } from "@/shared/business-details/server";
import { HeroSection } from "./components/hero-section";
import { TimelineFeatures } from "./components/timeline-features";
import { StickyScrollLearning } from "./components/sticky-scroll-learning";
import { TestimonialsSection } from "./components/testimonials-section";
import {
  ProgramSection,
  AuthorSection,
  FaqSection,
  FinalCtaSection,
  FooterSection,
} from "./components/sections";
import { PlansShowcase } from "./components/plans-showcase";
import { KnowledgeBaseLinkSection } from "./components/knowledge-base-link-section";
import { LevelTestLinkSection } from "./components/level-test-link-section";
import { FloatingTelegram } from "./components/floating-telegram";
import { LandingGrowthTracker } from "./components/landing-growth-tracker";

export default async function PlatformLanding() {
  const businessDetails = await loadBusinessDetails();
  return (
    <div className="min-h-svh bg-[#0A0A0B] text-[#FAFAFA] antialiased selection:bg-[#6BADA5]/30">
      <LandingGrowthTracker />
      <HeroSection />

      <div className="bg-[#0E0E11]">
        <TimelineFeatures />
      </div>

      <ProgramSection />

      <LevelTestLinkSection />

      <div className="border-t border-white/[0.04] bg-[#0E0E11]">
        <StickyScrollLearning />
      </div>

      <TestimonialsSection />

      <div className="border-t border-white/[0.04] bg-[#0E0E11]">
        <AuthorSection />
      </div>

      <PlansShowcase />

      <KnowledgeBaseLinkSection />

      <div className="border-t border-white/[0.04] bg-[#0E0E11]">
        <FaqSection />
      </div>

      <FinalCtaSection />
      <FooterSection businessDetails={businessDetails} />

      <FloatingTelegram />
    </div>
  );
}
