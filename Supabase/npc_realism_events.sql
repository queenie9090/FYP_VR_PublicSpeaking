-- Requires body_animation_events.sql and the existing shared tables. Additive.
BEGIN;
-- Preserve existing rows while enforcing the requested rule for new/updated rows.
-- Validate the constraint separately after reviewing any pre-existing exceptions.
DO $$
BEGIN
 IF NOT EXISTS (
     SELECT 1 FROM pg_constraint
     WHERE conrelid = 'public.npc_emotion_log'::regclass
       AND conname = 'npc_emotion_neutral_intensity_zero'
 ) THEN
  ALTER TABLE public.npc_emotion_log
   ADD CONSTRAINT npc_emotion_neutral_intensity_zero
   CHECK (current_emotion <> 'NEUTRAL' OR emotion_intensity = 0) NOT VALID;
 END IF;
END $$;
CREATE TABLE IF NOT EXISTS public.eye_behaviour_event (
 eye_event_id uuid PRIMARY KEY, session_id uuid NOT NULL REFERENCES public.session(session_id) ON DELETE CASCADE,
 turn_id uuid, npc_id text NOT NULL, npc_state text, emotion text, gaze_target text,
 eye_behaviour text NOT NULL, blink_event boolean NOT NULL DEFAULT false, timestamp timestamptz NOT NULL,
 FOREIGN KEY (session_id, turn_id) REFERENCES public.dialogue_turn(session_id, turn_id) ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS public.facial_expression_event (
 expression_event_id uuid PRIMARY KEY, session_id uuid NOT NULL REFERENCES public.session(session_id) ON DELETE CASCADE,
 turn_id uuid, npc_id text NOT NULL, emotion text NOT NULL CHECK (emotion IN ('HAPPY','SAD','ANGRY','NERVOUS','THINKING','NEUTRAL')),
 emotion_intensity integer NOT NULL CHECK (emotion_intensity BETWEEN 0 AND 100 AND (emotion <> 'NEUTRAL' OR emotion_intensity = 0)),
 expression_type text NOT NULL, npc_state text, micro_expression text, timestamp timestamptz NOT NULL,
 FOREIGN KEY (session_id, turn_id) REFERENCES public.dialogue_turn(session_id, turn_id) ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS public.voice_event (
 voice_event_id uuid PRIMARY KEY, session_id uuid NOT NULL REFERENCES public.session(session_id) ON DELETE CASCADE,
 turn_id uuid NOT NULL, npc_id text NOT NULL, emotion text, emotion_intensity integer CHECK (emotion_intensity BETWEEN 0 AND 100),
 voice_style text, dialogue_text text NOT NULL, tts_status text NOT NULL, timestamp timestamptz NOT NULL,
 FOREIGN KEY (session_id, turn_id) REFERENCES public.dialogue_turn(session_id, turn_id) ON DELETE CASCADE
);
DO $$
DECLARE tab text;
BEGIN
 FOREACH tab IN ARRAY ARRAY['eye_behaviour_event','facial_expression_event','voice_event'] LOOP
  EXECUTE format('CREATE INDEX IF NOT EXISTS %I ON public.%I (session_id, timestamp)', tab || '_session_time', tab);
  EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', tab);
  EXECUTE format('GRANT SELECT ON public.%I TO authenticated', tab);
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename=tab AND policyname='owner_read') THEN
   EXECUTE format('CREATE POLICY owner_read ON public.%I FOR SELECT TO authenticated USING (EXISTS (SELECT 1 FROM public.session s WHERE s.session_id = %I.session_id AND s.user_id = auth.uid()))', tab, tab);
  END IF;
 END LOOP;
END $$;
CREATE OR REPLACE FUNCTION public.save_training_session_v3(payload jsonb)
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
    eye public.eye_behaviour_event;
    face public.facial_expression_event;
    voice public.voice_event;
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
        IF NOT EXISTS (
            SELECT 1 FROM public.performance_metric
            WHERE metric_id = (payload->'metric'->>'metric_id')::uuid AND session_id = s.session_id
        ) THEN RAISE EXCEPTION 'Finalized session does not contain the submitted metric'; END IF;
        FOR item IN SELECT value FROM jsonb_array_elements(COALESCE(payload->'turns', '[]'::jsonb)) LOOP
            IF NOT EXISTS (SELECT 1 FROM public.dialogue_turn WHERE turn_id = (item->>'turn_id')::uuid AND session_id = s.session_id) THEN
                RAISE EXCEPTION 'Finalized session does not contain the submitted dialogue turn';
            END IF;
        END LOOP;
        FOR item IN SELECT value FROM jsonb_array_elements(COALESCE(payload->'emotions', '[]'::jsonb)) LOOP
            IF NOT EXISTS (
                SELECT 1 FROM public.npc_emotion_log e JOIN public.dialogue_turn t ON t.turn_id = e.turn_id
                WHERE e.emotion_log_id = (item->>'emotion_log_id')::uuid AND t.session_id = s.session_id
            ) THEN RAISE EXCEPTION 'Finalized session does not contain the submitted emotion'; END IF;
        END LOOP;
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
        FOR item IN SELECT value FROM jsonb_array_elements(COALESCE(payload->'eye_events', '[]'::jsonb)) LOOP
            IF NOT EXISTS (SELECT 1 FROM public.eye_behaviour_event WHERE eye_event_id = (item->>'eye_event_id')::uuid AND session_id = s.session_id) THEN
                RAISE EXCEPTION 'Finalized session does not contain the submitted eye event';
            END IF;
        END LOOP;
        FOR item IN SELECT value FROM jsonb_array_elements(COALESCE(payload->'facial_events', '[]'::jsonb)) LOOP
            IF NOT EXISTS (SELECT 1 FROM public.facial_expression_event WHERE expression_event_id = (item->>'expression_event_id')::uuid AND session_id = s.session_id) THEN
                RAISE EXCEPTION 'Finalized session does not contain the submitted facial event';
            END IF;
        END LOOP;
        FOR item IN SELECT value FROM jsonb_array_elements(COALESCE(payload->'voice_events', '[]'::jsonb)) LOOP
            IF NOT EXISTS (SELECT 1 FROM public.voice_event WHERE voice_event_id = (item->>'voice_event_id')::uuid AND session_id = s.session_id) THEN
                RAISE EXCEPTION 'Finalized session does not contain the submitted voice event';
            END IF;
        END LOOP;
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
    FOR item IN SELECT value FROM jsonb_array_elements(COALESCE(payload->'eye_events', '[]'::jsonb)) LOOP
        eye := jsonb_populate_record(NULL::public.eye_behaviour_event, item);
        IF eye.session_id IS DISTINCT FROM s.session_id THEN
            RAISE EXCEPTION 'eye_behaviour_event session mismatch';
        END IF;
        IF eye.turn_id IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM public.dialogue_turn WHERE turn_id = eye.turn_id AND session_id = s.session_id
        ) THEN RAISE EXCEPTION 'eye_behaviour_event turn mismatch'; END IF;
        INSERT INTO public.eye_behaviour_event SELECT eye.*;
    END LOOP;
    FOR item IN SELECT value FROM jsonb_array_elements(COALESCE(payload->'facial_events', '[]'::jsonb)) LOOP
        face := jsonb_populate_record(NULL::public.facial_expression_event, item);
        IF face.session_id IS DISTINCT FROM s.session_id THEN
            RAISE EXCEPTION 'facial_expression_event session mismatch';
        END IF;
        IF face.turn_id IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM public.dialogue_turn WHERE turn_id = face.turn_id AND session_id = s.session_id
        ) THEN RAISE EXCEPTION 'facial_expression_event turn mismatch'; END IF;
        INSERT INTO public.facial_expression_event SELECT face.*;
    END LOOP;
    FOR item IN SELECT value FROM jsonb_array_elements(COALESCE(payload->'voice_events', '[]'::jsonb)) LOOP
        voice := jsonb_populate_record(NULL::public.voice_event, item);
        IF voice.session_id IS DISTINCT FROM s.session_id THEN
            RAISE EXCEPTION 'voice_event session mismatch';
        END IF;
        IF voice.turn_id IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM public.dialogue_turn WHERE turn_id = voice.turn_id AND session_id = s.session_id
        ) THEN RAISE EXCEPTION 'voice_event turn mismatch'; END IF;
        INSERT INTO public.voice_event SELECT voice.*;
    END LOOP;
    RETURN s.session_id;
END;
$$;

REVOKE ALL ON FUNCTION public.save_training_session_v3(jsonb) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.save_training_session_v3(jsonb) TO authenticated;
COMMIT;
