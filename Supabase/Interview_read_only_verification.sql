-- READ-ONLY QUERIES. Run individual sections in Supabase SQL Editor.
-- This file does not create test records, change policies, or modify grants.

-- Effective table privileges for API roles (including inherited grants).
-- Column-level privileges can differ; inspect them separately below.
SELECT t.table_name, r.role_name,
       has_table_privilege(r.role_name, format('public.%I', t.table_name), 'SELECT') AS can_select,
       has_table_privilege(r.role_name, format('public.%I', t.table_name), 'INSERT') AS can_insert,
       has_table_privilege(r.role_name, format('public.%I', t.table_name), 'UPDATE') AS can_update,
       has_table_privilege(r.role_name, format('public.%I', t.table_name), 'DELETE') AS can_delete
FROM (VALUES ('session'), ('dialogue_turn'), ('performance_metric'), ('npc_emotion_log'),
             ('body_animation_event'), ('eye_behaviour_event'), ('facial_expression_event'), ('voice_event')) AS t(table_name)
CROSS JOIN (VALUES ('anon'), ('authenticated')) AS r(role_name)
ORDER BY t.table_name, r.role_name;

-- Explicit column grants may allow narrower access even if table-level access is false.
SELECT table_name, column_name, grantee, privilege_type
FROM information_schema.column_privileges
WHERE table_schema = 'public'
  AND table_name IN ('session','dialogue_turn','performance_metric','npc_emotion_log',
                     'body_animation_event','eye_behaviour_event','facial_expression_event','voice_event')
  AND grantee IN ('PUBLIC','anon','authenticated')
ORDER BY table_name, column_name, grantee, privilege_type;

-- RPC owner and security context: confirm whether it owns the target tables or
-- has BYPASSRLS before expecting the proposed owner-only RLS draft to work.
SELECT p.proname, pg_get_userbyid(p.proowner) AS function_owner,
       p.prosecdef AS security_definer, r.rolbypassrls, r.rolsuper
FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
JOIN pg_roles r ON r.oid = p.proowner
WHERE n.nspname = 'public'
  AND p.proname IN ('save_training_session','save_training_session_v2','save_training_session_v3')
ORDER BY p.proname;

SELECT c.relname AS table_name, pg_get_userbyid(c.relowner) AS table_owner,
       c.relrowsecurity AS rls_enabled, c.relforcerowsecurity AS force_rls
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public' AND c.relname IN
 ('session','dialogue_turn','performance_metric','npc_emotion_log','body_animation_event',
  'eye_behaviour_event','facial_expression_event','voice_event')
ORDER BY c.relname;

-- After an explicitly authorized isolated test, replace the UUID below with
-- that exact test session ID. Run separately for Completed and Incomplete.
WITH target AS (SELECT 'REPLACE-WITH-TEST-SESSION-UUID'::uuid AS id)
SELECT s.session_id, s.scenario_type, s.status, s.start_time, s.end_time,
       (SELECT count(*) FROM public.performance_metric m WHERE m.session_id=s.session_id) AS metric_count,
       (SELECT count(*) FROM public.dialogue_turn t WHERE t.session_id=s.session_id) AS turn_count,
       (SELECT count(*) FROM public.npc_emotion_log e JOIN public.dialogue_turn t ON t.turn_id=e.turn_id WHERE t.session_id=s.session_id) AS emotion_count,
       (SELECT count(*) FROM public.body_animation_event e WHERE e.session_id=s.session_id) AS body_count,
       (SELECT count(*) FROM public.eye_behaviour_event e WHERE e.session_id=s.session_id) AS eye_count,
       (SELECT count(*) FROM public.facial_expression_event e WHERE e.session_id=s.session_id) AS face_count,
       (SELECT count(*) FROM public.voice_event e WHERE e.session_id=s.session_id) AS voice_count
FROM public.session s JOIN target ON target.id=s.session_id;

-- Turn order and emotion linkage, without displaying participant transcripts.
WITH target AS (SELECT 'REPLACE-WITH-TEST-SESSION-UUID'::uuid AS id)
SELECT t.turn_sequence, t.turn_id, e.emotion_log_id, e.npc_name, e.previous_emotion,
       e.current_emotion, e.emotion_intensity, e.recorded_at
FROM public.dialogue_turn t JOIN target ON target.id=t.session_id
LEFT JOIN public.npc_emotion_log e ON e.turn_id=t.turn_id
ORDER BY t.turn_sequence, e.recorded_at, e.emotion_log_id;

-- Per-test-session duplicates. These checks should return zero rows.
WITH target AS (SELECT 'REPLACE-WITH-TEST-SESSION-UUID'::uuid AS id)
SELECT t.turn_sequence, count(*) AS duplicates
FROM public.dialogue_turn t JOIN target ON target.id=t.session_id
GROUP BY t.turn_sequence HAVING count(*) > 1;

WITH target AS (SELECT 'REPLACE-WITH-TEST-SESSION-UUID'::uuid AS id)
SELECT e.turn_id, e.npc_name, count(*) AS duplicates
FROM public.npc_emotion_log e JOIN public.dialogue_turn t ON t.turn_id=e.turn_id
JOIN target ON target.id=t.session_id
GROUP BY e.turn_id, e.npc_name HAVING count(*) > 1;

-- Behaviour events by type, NPC, and linked turn; NULL turn means session-only.
WITH target AS (SELECT 'REPLACE-WITH-TEST-SESSION-UUID'::uuid AS id), events AS (
 SELECT 'body' AS kind, session_id, turn_id, npc_id FROM public.body_animation_event
 UNION ALL SELECT 'eye', session_id, turn_id, npc_id FROM public.eye_behaviour_event
 UNION ALL SELECT 'face', session_id, turn_id, npc_id FROM public.facial_expression_event
 UNION ALL SELECT 'voice', session_id, turn_id, npc_id FROM public.voice_event
)
SELECT e.kind, e.npc_id, t.turn_sequence, e.turn_id, count(*) AS event_count
FROM events e JOIN target ON target.id=e.session_id
LEFT JOIN public.dialogue_turn t ON t.session_id=e.session_id AND t.turn_id=e.turn_id
GROUP BY e.kind, e.npc_id, t.turn_sequence, e.turn_id
ORDER BY e.kind, t.turn_sequence NULLS FIRST, e.npc_id;

-- Distribution counts are independent of the real-time intensity values.
WITH target AS (SELECT 'REPLACE-WITH-TEST-SESSION-UUID'::uuid AS id), labels AS (
 SELECT unnest(ARRAY['HAPPY','SAD','ANGRY','NERVOUS','THINKING','NEUTRAL']) AS emotion
), counts AS (
 SELECT e.current_emotion, count(*) AS n
 FROM public.npc_emotion_log e JOIN public.dialogue_turn t ON t.turn_id=e.turn_id
 JOIN target ON target.id=t.session_id GROUP BY e.current_emotion
)
SELECT labels.emotion, COALESCE(counts.n,0) AS occurrences,
 CASE WHEN sum(COALESCE(counts.n,0)) OVER () = 0 THEN 0
      ELSE round(100.0 * COALESCE(counts.n,0) / sum(COALESCE(counts.n,0)) OVER (), 2) END AS percent
FROM labels LEFT JOIN counts ON counts.current_emotion=labels.emotion
ORDER BY labels.emotion;
