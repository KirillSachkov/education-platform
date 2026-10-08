import { describe, expect, it } from "vitest";
import { buildTrainerHubStats, getWeakTrainerTopics } from "../hub-metrics";

describe("trainer hub metrics", () => {
  it("summarizes mastery and session history for the cockpit", () => {
    const stats = buildTrainerHubStats(
      [
        {
          topicId: "async",
          masteryPercent: 60,
          coveragePercent: 70,
          isWeak: false,
          answersCount: 12,
          studiedCount: 8,
          mistakesCount: 1,
          lastPractisedAt: "2026-06-17T12:00:00Z",
        },
        {
          topicId: "sql",
          masteryPercent: 30,
          coveragePercent: 30,
          isWeak: true,
          answersCount: 4,
          studiedCount: 2,
          mistakesCount: 3,
          lastPractisedAt: "2026-06-18T09:00:00Z",
        },
      ],
      [
        {
          id: "s1",
          mode: "MOCK",
          status: "COMPLETED",
          topicIds: ["async"],
          scorePercent: 80,
          answeredCount: 8,
          totalCount: 10,
          startedAt: "2026-06-17T12:00:00Z",
          completedAt: "2026-06-17T13:00:00Z",
          timeLimitSeconds: null,
          gradingStatus: "NOT_REQUIRED",
        },
        {
          id: "s2",
          mode: "LEARN",
          status: "IN_PROGRESS",
          topicIds: ["sql"],
          scorePercent: null,
          answeredCount: 2,
          totalCount: 5,
          startedAt: "2026-06-18T09:00:00Z",
          completedAt: null,
          timeLimitSeconds: null,
          gradingStatus: "NOT_REQUIRED",
        },
      ],
      4, // 4 published topics in scope — readiness is breadth-weighted, not mean-over-touched
    );

    // Coverage-damped over ALL published topics: (70 + 30) / 4 = 25 (not the old mean(60,30)=45).
    expect(stats.readinessPercent).toBe(25);
    expect(stats.topicCount).toBe(4);
    expect(stats.weakCount).toBe(1);
    expect(stats.answeredCount).toBe(10);
    expect(stats.completedSessions).toBe(1);
    expect(stats.averageScorePercent).toBe(80);
  });

  it("returns stable zero stats without data", () => {
    expect(buildTrainerHubStats([], [])).toEqual({
      readinessPercent: 0,
      topicCount: 0,
      weakCount: 0,
      answeredCount: 0,
      completedSessions: 0,
      averageScorePercent: null,
    });
  });

  it("sorts weak topics by lowest mastery and resolves titles", () => {
    const weakTopics = getWeakTrainerTopics(
      [
        {
          topicId: "sql",
          masteryPercent: 35,
          coveragePercent: 35,
          isWeak: true,
          answersCount: 5,
          studiedCount: 3,
          mistakesCount: 2,
          lastPractisedAt: "2026-06-18T09:00:00Z",
        },
        {
          topicId: "asp",
          masteryPercent: 10,
          coveragePercent: 10,
          isWeak: true,
          answersCount: 2,
          studiedCount: 1,
          mistakesCount: 4,
          lastPractisedAt: "2026-06-17T09:00:00Z",
        },
        {
          topicId: "docker",
          masteryPercent: 0,
          coveragePercent: 0,
          isWeak: true,
          answersCount: 0,
          studiedCount: 0,
          mistakesCount: 0,
          lastPractisedAt: "2026-06-18T00:00:00Z",
        },
      ],
      new Map([
        ["sql", { slug: "sql", title: "EF Core и SQL" }],
        ["asp", { slug: "asp-net-core", title: "ASP.NET Core" }],
        ["docker", { slug: "docker-compose", title: "Docker Compose" }],
      ]),
    );

    expect(weakTopics).toEqual([
      { topicId: "asp", slug: "asp-net-core", title: "ASP.NET Core", masteryPercent: 10 },
      { topicId: "sql", slug: "sql", title: "EF Core и SQL", masteryPercent: 35 },
    ]);
  });

  it("does not count unattempted topics as weak", () => {
    const mastery = [
      {
        topicId: "docker",
        masteryPercent: 0,
        coveragePercent: 0,
        isWeak: true,
        answersCount: 0,
        studiedCount: 0,
        mistakesCount: 0,
        lastPractisedAt: "2026-06-18T00:00:00Z",
      },
    ];

    expect(buildTrainerHubStats(mastery, []).weakCount).toBe(0);
    expect(
      getWeakTrainerTopics(
        mastery,
        new Map([["docker", { slug: "docker-compose", title: "Docker Compose" }]]),
      ),
    ).toEqual([]);
  });

  it("counts high mistake pressure as a topic to improve", () => {
    const mastery = [
      {
        topicId: "asp",
        masteryPercent: 72,
        coveragePercent: 72,
        isWeak: false,
        answersCount: 6,
        studiedCount: 5,
        mistakesCount: 3,
        lastPractisedAt: "2026-06-18T09:00:00Z",
      },
    ];

    expect(buildTrainerHubStats(mastery, []).weakCount).toBe(1);
    expect(
      getWeakTrainerTopics(
        mastery,
        new Map([["asp", { slug: "asp-net-core", title: "ASP.NET Core" }]]),
      ),
    ).toEqual([
      { topicId: "asp", slug: "asp-net-core", title: "ASP.NET Core", masteryPercent: 72 },
    ]);
  });
});
