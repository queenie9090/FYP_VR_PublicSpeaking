-- Apply after save_training_session.sql. No rows or existing functions are removed.
BEGIN;
CREATE UNIQUE INDEX IF NOT EXISTS dialogue_turn_session_turn_unique
ON public.dialogue_turn(session_id, turn_id);

CREATE TABLE IF NOT EXISTS public.body_animation_event (
    animation_event_id uuid PRIMARY KEY,
    npc_id text NOT NULL CHECK (length(trim(npc_id)) > 0),
    npc_type text NOT NULL CHECK (npc_type IN ('INTERVIEWER', 'AMBIENT')),
    scene_type text NOT NULL,
    npc_activity text NOT NULL,
    emotion text NOT NULL CHECK (emotion IN ('HAPPY','SAD','ANGRY','NERVOUS','THINKING','NEUTRAL')),
    animation_name text NOT NULL,
    timestamp timestamptz NOT NULL,
    session_id uuid NOT NULL REFERENCES public.session(session_id) ON DELETE CASCADE,
    turn_id uuid,
    FOREIGN KEY (session_id, turn_id)
        REFERENCES public.dialogue_turn(session_id, turn_id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS body_animation_event_session_time
ON public.body_animation_event(session_id, timestamp);
ALTER TABLE public.body_animation_event ENABLE ROW LEVEL SECURITY;
GRANT SELECT ON public.body_animation_event TO authenticated;
DROP POLICY IF EXISTS body_animation_owner_read ON public.body_animation_event;
CREATE POLICY body_animation_owner_read ON public.body_animation_event
FOR SELECT TO authenticated USING (EXISTS (
    SELECT 1 FROM public.session s
    WHERE s.session_id = body_animation_event.session_id AND s.user_id = auth.uid()
));

CREATE OR REPLACE FUNCTION public.save_training_session_v2(payload jsonb)
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
    b public.body_animation_event;
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
        IF EXISTS (
            SELECT 1 FROM jsonb_array_elements(COALESCE(payload->'body_events', '[]'::jsonb)) x
            WHERE NOT EXISTS (
                SELECT 1 FROM public.body_animation_event b
                WHERE b.animation_event_id = (x->>'animation_event_id')::uuid
                  AND b.session_id = s.session_id
            )
        ) THEN
            RAISE EXCEPTION 'Finalized session does not contain the submitted animation events';
        END IF;
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
    FOR item IN SELECT value FROM jsonb_array_elements(COALESCE(payload->'body_events', '[]'::jsonb)) LOOP
        b := jsonb_populate_record(NULL::public.body_animation_event, item);
        IF b.session_id IS DISTINCT FROM s.session_id THEN
            RAISE EXCEPTION 'Body animation session mismatch';
        END IF;
        IF b.turn_id IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM public.dialogue_turn WHERE turn_id = b.turn_id AND session_id = s.session_id
        ) THEN
            RAISE EXCEPTION 'Body animation dialogue/session mismatch';
        END IF;
        INSERT INTO public.body_animation_event SELECT b.*;
    END LOOP;
    RETURN s.session_id;
END;
$$;

REVOKE ALL ON FUNCTION public.save_training_session_v2(jsonb) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.save_training_session_v2(jsonb) TO authenticated;
COMMIT;
