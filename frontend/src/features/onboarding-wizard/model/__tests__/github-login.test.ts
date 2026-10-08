import { describe, expect, it } from "vitest";
import { extractGithubLogin } from "../github-login";

describe("extractGithubLogin", () => {
  it("returns login from canonical github URL", () => {
    expect(extractGithubLogin("https://github.com/octocat")).toBe("octocat");
  });

  it("returns login from URL with trailing slash", () => {
    expect(extractGithubLogin("https://github.com/octocat/")).toBe("octocat");
  });

  it("returns login from URL with path suffix", () => {
    expect(extractGithubLogin("https://github.com/octocat/repo")).toBe("octocat");
  });

  it("returns login from URL with query string", () => {
    expect(extractGithubLogin("https://github.com/octocat?ref=main")).toBe("octocat");
  });

  it("returns null for null input", () => {
    expect(extractGithubLogin(null)).toBeNull();
  });

  it("returns null for empty string", () => {
    expect(extractGithubLogin("")).toBeNull();
  });

  it("returns null for non-github URL", () => {
    expect(extractGithubLogin("https://example.com/octocat")).toBeNull();
  });

  it("returns null for github URL without login segment", () => {
    expect(extractGithubLogin("https://github.com/")).toBeNull();
  });

  it("preserves login case (GitHub usernames are case-insensitive but display preserves)", () => {
    expect(extractGithubLogin("https://github.com/Octo-Cat")).toBe("Octo-Cat");
  });

  it("works with subdomain-less form (theoretical edge case)", () => {
    expect(extractGithubLogin("github.com/octocat")).toBe("octocat");
  });
});
