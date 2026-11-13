-- REVIEW DRAFT ONLY. DO NOT RUN WITHOUT PROJECT-OWNER AND TEAMMATE APPROVAL.
--
-- Proposed student-owner access for the three shared tables. The existing
-- SECURITY DEFINER save_training_session* RPCs remain the only client write
-- path. Verify beforehand that their actual owner can bypass RLS and has all
-- required table privileges, and that no other teammate uses direct writes.
--
-- IMPORTANT: This intentionally removes cohort-wide educator reads from these
-- tables. An authorized, server-enforced educator/cohort access design must be
-- reviewed and deployed separately before applying if that dashboard is needed.
-- Never use the Unity client's UserRole as a database authorization claim.
-- Take a reviewed backup and verify effective privileges and all existing
-- policies, including inherited roles and column grants, before applying.
-- The guard rejects unexpected additional policies on these tables because
-- permissive policies combine with OR and could defeat owner isolation.

BEGIN;

DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM pg_policies
    WHERE schemaname = 'public'
      AND tablename IN ('session', 'dialogue_turn', 'performance_metric')
      AND NOT (
        (tablename = 'session' AND policyname = 'Allow public access to session') OR
        (tablename = 'dialogue_turn' AND policyname = 'Allow public access to dialogue_turn') OR
        (tablename = 'performance_metric' AND policyname = 'Allow public access to performance_metric')
      )
  ) THEN
    RAISE EXCEPTION 'Unexpected policy found; review all policies before applying this draft';
  END IF;
END $$;

REVOKE SELECT, INSERT, UPDATE, DELETE ON public.session, public.dialogue_turn,
  public.performance_metric FROM PUBLIC, anon;
REVOKE INSERT, UPDATE, DELETE ON public.session, public.dialogue_turn,
  public.performance_metric FROM authenticated;
GRANT SELECT ON public.session, public.dialogue_turn, public.performance_metric TO authenticated;

DROP POLICY IF EXISTS "Allow public access to session" ON public.session;
DROP POLICY IF EXISTS "Allow public access to dialogue_turn" ON public.dialogue_turn;
DROP POLICY IF EXISTS "Allow public access to performance_metric" ON public.performance_metric;

CREATE POLICY session_owner_read ON public.session
  FOR SELECT TO authenticated USING (user_id = auth.uid());
CREATE POLICY dialogue_turn_owner_read ON public.dialogue_turn
  FOR SELECT TO authenticated USING (EXISTS (
    SELECT 1 FROM public.session s
    WHERE s.session_id = dialogue_turn.session_id AND s.user_id = auth.uid()
  ));
CREATE POLICY performance_metric_owner_read ON public.performance_metric
  FOR SELECT TO authenticated USING (EXISTS (
    SELECT 1 FROM public.session s
    WHERE s.session_id = performance_metric.session_id AND s.user_id = auth.uid()
  ));

COMMIT;
