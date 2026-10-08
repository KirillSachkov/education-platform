import { describe, expect, it } from "vitest";
import { isGitHubPullRequestUrl } from "./lib";

describe("isGitHubPullRequestUrl (#668)", () => {
  it.each([
    "https://github.com/test/repo/pull/1",
    "https://github.com/test/repo/pull/65",
    "https://github.com/test/repo/pull/1/", // trailing slash
    "https://github.com/maxembo/DirectoryService/pull/65#pullrequestreview-4580609186", // fragment
    "https://github.com/test/repo/pull/1?diff=split", // query
    "https://github.com/test/repo/pull/1/files", // sub-path
    "https://github.com/uluanaro/DirectoryService/pull/6/changes/426891aae3", // changes sub-path
    "HTTPS://GitHub.com/Test/Repo/pull/9", // case-insensitive
    "  https://github.com/test/repo/pull/1  ", // trims surrounding whitespace
  ])("accepts a valid PR URL: %s", (url) => {
    expect(isGitHubPullRequestUrl(url)).toBe(true);
  });

  it.each([
    "https://github.com/test/repo", // no /pull/
    "https://github.com/test/repo/pull/", // missing number
    "https://github.com/test/repo/pull/abc", // non-numeric
    "https://github.com/wolonee/DirectoryService/pull/new/DS-F15", // «create PR» page — root cause #718
    "https://github.com/test/repo/tree/main", // branch, not pull
    "https://github.com/test/repo/issues/1", // issues, not pull
    "https://gitlab.com/test/repo/pull/1", // wrong host
    "http://github.com/test/repo/pull/1", // not https
    "https://example.com/manual-review", // unrelated url
    "", // empty
  ])("rejects an invalid URL: %s", (url) => {
    expect(isGitHubPullRequestUrl(url)).toBe(false);
  });
});
