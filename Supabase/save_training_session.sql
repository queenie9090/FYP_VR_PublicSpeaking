-- Run manually in the Supabase SQL Editor after creating npc_emotion_log.
-- Adds an authenticated atomic save function and owner-only emotion reads.
-- Existing tables, records, and dashboard policies are preserved.
BEGIN;

CREATE OR REPLACE FUNCTION public.save_training_session(payload jsonb)
RETURNS uuid
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = ''
AS $$
DECLARE
    s public.session;
    m public.performance_metric;
    t public.dialogue_turn;
    e public.npc_emotion_log;
    item jsonb;
    existing_owner uuid;
BEGIN
    s := jsonb_populate_record(NULL::public.session, payload->'session');
    IF auth.uid() IS NULL OR s.user_id IS DISTINCT FROM auth.uid() THEN
        RAISE EXCEPTION 'Session owner must match authenticated user' USING ERRCODE = '42501';
    END IF;
    IF s.session_id IS NULL OR s.start_time IS NULL OR s.end_time IS NULL THEN
        RAISE EXCEPTION 'Session ID and timestamps are required';
    END IF;
    IF s.status NOT IN ('Completed', 'Incomplete') THEN
        RAISE EXCEPTION 'Invalid session status';
    END IF;
    -- Serialize retries for this UUID, including simultaneous requests.
    PERFORM pg_advisory_xact_lock(hashtextextended(s.session_id::text, 0));
    SELECT user_id INTO existing_owner FROM public.session WHERE session_id = s.session_id;
    IF FOUND THEN
        IF existing_owner IS DISTINCT FROM auth.uid() THEN
            RAISE EXCEPTION 'Session belongs to another user' USING ERRCODE = '42501';
        END IF;
        -- Finalized snapshots are immutable. A retry returns the original confirmation.
        RETURN s.session_id;
    END IF;

    INSERT INTO public.session SELECT s.*;
    m := jsonb_populate_record(NULL::public.performance_metric, payload->'metric');
    IF m.session_id IS DISTINCT FROM s.session_id THEN
        RAISE EXCEPTION 'Metric session mismatch';
    END IF;
    INSERT INTO public.performance_metric SELECT m.*;

    FOR item IN SELECT value FROM jsonb_array_elements(payload->'turns') LOOP
        t := jsonb_populate_record(NULL::public.dialogue_turn, item);
        IF t.session_id IS DISTINCT FROM s.session_id THEN
            RAISE EXCEPTION 'Dialogue session mismatch';
        END IF;
        t.turn_timestamp := COALESCE(t.turn_timestamp, s.end_time);
        INSERT INTO public.dialogue_turn SELECT t.*;
    END LOOP;

    FOR item IN SELECT value FROM jsonb_array_elements(payload->'emotions') LOOP
        e := jsonb_populate_record(NULL::public.npc_emotion_log, item);
        IF NOT EXISTS (
            SELECT 1 FROM public.dialogue_turn
            WHERE turn_id = e.turn_id AND session_id = s.session_id
        ) THEN
            RAISE EXCEPTION 'Emotion dialogue/session mismatch';
        END IF;
        INSERT INTO public.npc_emotion_log SELECT e.*;
    END LOOP;
    RETURN s.session_id;
END;
$$;

REVOKE ALL ON FUNCTION public.save_training_session(jsonb) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.save_training_session(jsonb) TO authenticated;
GRANT SELECT ON public.npc_emotion_log TO authenticated;
DROP POLICY IF EXISTS emotion_owner_read ON public.npc_emotion_log;
CREATE POLICY emotion_owner_read ON public.npc_emotion_log
FOR SELECT TO authenticated
USING (EXISTS (
    SELECT 1 FROM public.dialogue_turn t
    JOIN public.session s ON s.session_id = t.session_id
    WHERE t.turn_id = npc_emotion_log.turn_id AND s.user_id = auth.uid()
));
COMMIT;
