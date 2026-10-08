-- #532: backfill published_at для курсов, опубликованных до появления колонки.
-- created_at (а не updated_at) — чтобы старые курсы не попали в «новое за неделю»
-- первым же дайджестом. ARCHIVED тоже были опубликованы когда-то.
UPDATE courses
SET published_at = created_at
WHERE status IN ('PUBLISHED', 'ARCHIVED')
  AND published_at IS NULL;
