-- Pre-migration audit for `course_ids[]` → `course_id` singular conversion.
-- Run against prod (and dev) BEFORE applying PlanSingleCourseId migration.
-- Expected most-likely result: all counts = 0.
SELECT
    (SELECT count(*) FROM access.plans
     WHERE tier = 'COURSE'
       AND array_length(course_ids, 1) > 1) AS multi_course_plans,

    (SELECT count(*) FROM access.plans
     WHERE tier = 'COURSE'
       AND (array_length(course_ids, 1) IS NULL OR array_length(course_ids, 1) = 0)) AS empty_course_id_plans,

    (SELECT count(*) FROM access.plans
     WHERE tier = 'LEARN_ALL' AND is_active = true) AS learn_all_active,

    (SELECT count(*) FROM access.plans
     WHERE tier <> 'COURSE'
       AND array_length(course_ids, 1) IS NOT NULL
       AND array_length(course_ids, 1) > 0) AS non_course_with_course_ids;

-- Interpretation:
--  multi_course_plans > 0      → migration requires per-row split; pause + ping owner
--  empty_course_id_plans > 0   → harmless after the relax-archived migration (PlanCheckConstraintRelaxArchived
--                                auto-archives them as a safety net); investigate provenance offline
--  learn_all_active > 0        → LEARN_ALL is deprecated at factory level; existing rows are preserved
--                                as read-path data but no new ones can be created. No action required.
--  non_course_with_course_ids  → unexpected; should be 0. If >0, the original `course_ids[]` had
--                                stray entries for non-COURSE tiers (legacy data) — these are lost
--                                during the singular-id rename. Verify before applying.
