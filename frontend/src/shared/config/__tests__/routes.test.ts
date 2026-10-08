import { describe, it, expect } from "vitest";
import { parseFromContext, routes } from "../routes";

describe("routes", () => {
  describe("static routes", () => {
    it("has correct login route", () => {
      expect(routes.login).toBe("/login");
    });

    it("has correct home route", () => {
      expect(routes.home).toBe("/home");
    });

    it("has correct landing route", () => {
      expect(routes.landing).toBe("/");
    });

    it("has correct myCourses route", () => {
      expect(routes.myCourses).toBe("/courses");
    });

    it("has correct forgotPassword route", () => {
      expect(routes.forgotPassword).toBe("/login/forgot-password");
    });

    it("has correct resetPassword route", () => {
      expect(routes.resetPassword).toBe("/login/reset-password");
    });

    it("has correct authError route", () => {
      expect(routes.authError).toBe("/auth-error");
    });

    it("has correct authorCourses route", () => {
      expect(routes.authorCourses).toBe("/author/courses");
    });

    it("has correct authorReview route", () => {
      expect(routes.authorReview).toBe("/author/review");
    });

    it("has correct profile route", () => {
      expect(routes.profile).toBe("/profile");
    });

    it("has correct adminUsers route", () => {
      expect(routes.adminUsers).toBe("/admin/users");
    });

    it("has correct pricing route", () => {
      expect(routes.pricing).toBe("/pricing");
    });

    it("builds knowledge base routes", () => {
      expect(routes.knowledgeBase).toBe("/knowledge-base");
      expect(routes.knowledgeBaseMaterial("article-1")).toBe("/knowledge-base/article-1");
      expect(routes.authorKnowledgeBase).toBe("/author/knowledge-base");
      expect(routes.authorKnowledgeBaseCreate).toBe("/author/knowledge-base/new");
      expect(routes.authorMaterialEdit("article-1")).toBe("/author/knowledge-base/edit/article-1");
    });
  });

  describe("dynamic routes (single-tenant flat URLs)", () => {
    it("builds courseOverview with courseSlug", () => {
      expect(routes.courseOverview("c1")).toBe("/courses/c1");
    });

    it("builds courseMaterial with courseSlug and materialId", () => {
      expect(routes.courseMaterial("c1", "m1")).toBe("/courses/c1/learn/m1");
    });

    it("builds courseIssue with courseSlug and issueId", () => {
      expect(routes.courseIssue("c1", "i1")).toBe("/courses/c1/issues/i1");
    });

    it("builds courseModule pointing to program with section query", () => {
      expect(routes.courseModule("c1", "m1")).toBe("/courses/c1/program?section=m1");
    });

    it("builds courseProject pointing to assignments page with section query", () => {
      expect(routes.courseProject("c1", "p1")).toBe("/courses/c1/assignments?section=p1");
    });

    it("builds courseCollectionDetail with courseSlug and collectionId", () => {
      expect(routes.courseCollectionDetail("c1", "col-1")).toBe("/courses/c1/collections/col-1");
    });

    it("builds authorCourseBuilder with courseSlug", () => {
      expect(routes.authorCourseBuilder("c1")).toBe("/author/courses/c1");
    });

    it("builds adminUserDetail with userId", () => {
      expect(routes.adminUserDetail("u1")).toBe("/admin/users/u1");
    });

    it("builds pricingPlanDetail with plan slug", () => {
      expect(routes.pricingPlanDetail("lifetime")).toBe("/pricing/lifetime");
    });

    it("builds materialDetail with materialId", () => {
      expect(routes.materialDetail("m1")).toBe("/knowledge-base/m1");
    });

    it("builds collectionDetail with collectionId", () => {
      expect(routes.collectionDetail("col-1")).toBe("/collections/col-1");
    });
  });

  describe("parseFromContext (collection breadcrumb regression — #276)", () => {
    it("returns null for empty input", () => {
      expect(parseFromContext(null)).toBeNull();
      expect(parseFromContext("")).toBeNull();
    });

    it("parses space collection: collection:slug::collectionId", () => {
      expect(parseFromContext("collection:sachkov::col-123")).toEqual({
        kind: "collection",
        slug: "sachkov",
        collectionId: "col-123",
      });
    });

    it("parses course collection: collection:slug:courseSlug:collectionId", () => {
      expect(parseFromContext("collection:sachkov:dotnet:col-123")).toEqual({
        kind: "collection",
        slug: "sachkov",
        courseSlug: "dotnet",
        collectionId: "col-123",
      });
    });

    it("returns null when collectionId is missing", () => {
      expect(parseFromContext("collection:sachkov")).toBeNull();
    });

    it("parses non-collection kinds back into their structured form", () => {
      expect(parseFromContext("space:sachkov")).toEqual({ kind: "space", slug: "sachkov" });
      expect(parseFromContext("space-kb:sachkov")).toEqual({
        kind: "space-knowledge-base",
        slug: "sachkov",
      });
      expect(parseFromContext("bookmarks:sachkov")).toEqual({
        kind: "bookmarks",
        slug: "sachkov",
      });
    });
  });
});
