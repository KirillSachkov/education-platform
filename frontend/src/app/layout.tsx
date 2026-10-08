import { ChunkErrorReloader, RegisterServiceWorker, WebVitalsReporter } from "@/shared/lib/pwa";
import { ThemeProvider } from "@/shared/providers/theme-provider";
import { APP_URL } from "@/shared/config/site";
import { CookieBanner } from "@/widgets/cookie-banner";
import { YandexMetrikaScript } from "@/widgets/yandex-metrika";
import type { Metadata, Viewport } from "next";
import { Inter, JetBrains_Mono, Sora } from "next/font/google";
import "./globals.css";

const inter = Inter({
  variable: "--font-inter",
  subsets: ["latin", "cyrillic"],
  preload: false,
});

const jetbrainsMono = JetBrains_Mono({
  variable: "--font-jetbrains-mono",
  subsets: ["latin", "cyrillic"],
  preload: false,
});

const sora = Sora({
  variable: "--font-sora",
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
  preload: false,
});

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  maximumScale: 5,
  viewportFit: "cover",
  themeColor: "#13120f",
};

export const metadata: Metadata = {
  metadataBase: new URL(`${APP_URL}/`),
  title: {
    template: "%s | SachkovLearn",
    default: "SachkovLearn",
  },
  description:
    "Информационно-консультационная платформа по программированию: курсы C#, .NET, ASP.NET Core, " +
    "микросервисы, DevOps, AI — с AI-ревью каждого PR и ответами автора.",
  keywords: [
    "курс C#",
    "курсы C#",
    "C# с нуля",
    "курс .NET",
    ".NET обучение",
    "ASP.NET Core курс",
    "C# разработка",
    "backend разработка",
    "fullstack .NET",
  ],
  // Search-console ownership tags — read from runtime server env so the owner can
  // set them without a frontend rebuild. Undefined => Next omits the tag entirely
  // (no-op until verification codes exist). Yandex.Webmaster / Google Search Console
  // also support DNS / HTML-file verification as an alternative to these meta tags.
  verification: {
    yandex: process.env.YANDEX_VERIFICATION_ID,
    google: process.env.GOOGLE_SITE_VERIFICATION_ID,
  },
  applicationName: "SachkovLearn",
  manifest: "/manifest.webmanifest",
  appleWebApp: {
    capable: true,
    title: "SachkovLearn",
    statusBarStyle: "black-translucent",
  },
  formatDetection: {
    telephone: false,
  },
  icons: {
    icon: [
      { url: "/favicon.ico", sizes: "any" },
      { url: "/icon.svg", type: "image/svg+xml" },
      { url: "/icons/icon-192.png", sizes: "192x192", type: "image/png" },
      { url: "/icons/icon-512.png", sizes: "512x512", type: "image/png" },
    ],
    apple: "/apple-touch-icon.png",
  },
  other: {
    "mobile-web-app-capable": "yes",
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    // `className="dark"` + `colorScheme:"dark"` оставлены на initial HTML, чтобы
    // SSR-разметка не моргала: next-themes сразу подменит класс в `<html>` если
    // пользователь выбрал другую тему. Без этого первый paint всегда был бы dark
    // для system-light юзеров.
    <html
      lang="ru"
      className="dark"
      style={{ colorScheme: "dark" }}
      data-scroll-behavior="smooth"
      suppressHydrationWarning
    >
      <body className={`${inter.variable} ${jetbrainsMono.variable} ${sora.variable} antialiased`}>
        <ThemeProvider attribute="class" defaultTheme="dark" enableSystem disableTransitionOnChange>
          {children}
          <CookieBanner />
          <YandexMetrikaScript />
          <ChunkErrorReloader />
          <RegisterServiceWorker />
          <WebVitalsReporter />
        </ThemeProvider>
      </body>
    </html>
  );
}
