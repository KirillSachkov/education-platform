import { describe, expect, it, vi } from "vitest";
import MaterialsRoutePage from "../page";

vi.mock("@/widgets/knowledge-base-view", () => ({
  KnowledgeBaseView: (props: { authorSlug?: string }) => (
    <div data-author-slug={props.authorSlug ?? ""} />
  ),
}));

describe("platform knowledge-base route", () => {
  it("renders the knowledge base without author scoping", () => {
    const element = MaterialsRoutePage();

    expect(element.props.authorSlug).toBeUndefined();
  });
});
