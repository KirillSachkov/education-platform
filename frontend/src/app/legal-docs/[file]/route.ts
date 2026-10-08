import { loadLegalPdf } from "@/shared/legal";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export async function GET(_request: Request, { params }: { params: Promise<{ file: string }> }) {
  const { file } = await params;
  const [, slug, version] = /^([a-z-]+)-(v\d+)\.pdf$/.exec(file) ?? [];
  const content = slug && version ? await loadLegalPdf(slug, version) : null;
  if (!content) return new Response("Document not found", { status: 404 });

  return new Response(content, {
    headers: {
      "Content-Type": "application/pdf",
      "Content-Disposition": `attachment; filename="${file}"`,
      "Cache-Control": "no-store",
    },
  });
}
