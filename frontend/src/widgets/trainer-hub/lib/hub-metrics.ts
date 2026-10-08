import type { TrainerSessionHistoryItem } from "@/entities/trainer-session";
import { computeInterviewReadiness, type TrainerTopicMastery } from "@/entities/trainer-topic";
import { getTrainerTopicStudyStatus } from "./topic-status";

export interface TrainerHubStats {
  readinessPercent: number;
  topicCount: number;
  weakCount: number;
  answeredCount: number;
  completedSessions: number;
  averageScorePercent: number | null;
}

export interface WeakTrainerTopic {
  topicId: string;
  slug: string;
  title: string;
  masteryPercent: number;
}

interface TopicTitleRef {
  slug: string;
  title: string;
}

export function buildTrainerHubStats(
  mastery: TrainerTopicMastery[],
  history: TrainerSessionHistoryItem[],
  totalPublishedTopics: number = mastery.length,
): TrainerHubStats {
  // Честная «готовность» — учитывает охват (нетронутые темы = 0), а не только глубину тронутых.
  // 0 как fallback, когда практики ещё нет (см. computeInterviewReadiness → percent=null).
  const readinessPercent = computeInterviewReadiness(mastery, totalPublishedTopics).percent ?? 0;
  const scored = history.filter((session) => session.scorePercent !== null);

  return {
    readinessPercent,
    topicCount: totalPublishedTopics,
    weakCount: mastery.filter((row) => isNeedsWork(row)).length,
    answeredCount: history.reduce((sum, session) => sum + session.answeredCount, 0),
    completedSessions: history.filter((session) => session.status === "COMPLETED").length,
    averageScorePercent:
      scored.length === 0
        ? null
        : Math.round(
            scored.reduce((sum, session) => sum + (session.scorePercent ?? 0), 0) /
              scored.length,
          ),
  };
}

export function getWeakTrainerTopics(
  mastery: TrainerTopicMastery[],
  topicMap: Map<string, TopicTitleRef>,
  limit = 3,
): WeakTrainerTopic[] {
  return mastery
    .filter(isNeedsWork)
    .sort((a, b) => a.masteryPercent - b.masteryPercent)
    .slice(0, limit)
    .flatMap((row) => {
      const topic = topicMap.get(row.topicId);
      if (!topic) return [];

      return [
        {
          topicId: row.topicId,
          slug: topic.slug,
          title: topic.title,
          masteryPercent: row.masteryPercent,
        },
      ];
    });
}

function isNeedsWork(row: TrainerTopicMastery): boolean {
  return (
    getTrainerTopicStudyStatus({
      masteryPercent: row.masteryPercent,
      isWeak: row.isWeak,
      answersCount: row.answersCount,
      studiedCount: row.studiedCount,
      mistakesCount: row.mistakesCount,
    }).kind === "needs-work"
  );
}
