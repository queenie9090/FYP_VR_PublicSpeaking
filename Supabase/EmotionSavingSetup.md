# Emotion saving setup and verification

## Required database setup

Run `save_training_session.sql` in the Supabase SQL Editor. It adds an authenticated,
transactional save function and an owner-only SELECT policy for npc_emotion_log.
It does not delete records or replace tables. The function inserts session, metric,
dialogue and emotion records in one transaction. Any insert failure rolls everything
back. Repeated requests with the same session UUID return the original UUID without
changing its final status or adding rows.

The function uses SECURITY DEFINER with an empty search path, explicitly checks
auth.uid(), checks child relationships, and grants execution only to authenticated.
Never put a service_role key in Unity.

Existing public ALL policies on the older tables are unchanged. They permit broader
access than the new emotion policy. A separate permissions review is needed to secure
those older tables without disrupting educator/dashboard access. The new emotion
read policy currently supports the session owner, not educator-wide access.

## Unity integration

Sign in through the existing login flow. UserSession retains the existing Supabase
client so refreshed access tokens remain available after scene changes. Restart Play
mode and sign in again after installing this code.

SessionResultSaver.ProcessAndSaveResults retains its existing parameters and adds an
optional sessionStatus argument. InteractionManager supplies Completed or Incomplete.
Tutorial sessions now save for authenticated users. Guest sessions still skip saving.
Exiting before starting never invokes the saver. A started session with zero turns
can save as Incomplete without inventing dialogue/emotions.

The persistent NPCEmotionSessionManager hosts save work. Session snapshots are copied
before asynchronous feedback generation. Failed HTTP payloads remain in memory with
their UUIDs and original final status, even if another scene or session is opened.
Call SupabaseManager.RetryPendingSaves(), or choose Retry Pending Session Saves from
that component's context menu during Play mode. The retry filters by signed-in owner.
Pending saves are not durable across application shutdown.

No additional Inspector wiring is required for emotion collection. The saver resolves
existing dependencies and uses the persistent manager as a fallback host.

## Field mapping

- Session uses the actual scenario_type, duration_seconds, and title-case status.
- Dialogue uses turn_id, turn_sequence, user_transcript, ai_response_text, session_id.
- Emotion uses emotion_log_id, turn_id, npc_name, previous_emotion, current_emotion,
  emotion_intensity, emotion_trigger, recorded_at.
- The database has no NPC entity/UUID. npc_name is the existing recorded identity.
  Give NPCs distinct names when multiple NPCs share a turn.
- Previous emotion is tracked per NPC. First emotion follows NEUTRAL.
- The explicit trigger is null unless a caller supplies one to RecordEmotion.
  Dialogue is available through turn_id; it is not labelled as a proven causal trigger.
- Intensity remains the conversation's existing temporary 60 (NEUTRAL 0).
  Emotion percentages should be calculated from record counts, not intensity.
- Dialogue timestamps currently fall back to the session end time in the function;
  emotion recorded_at is captured when the response is recorded.

## Verification in Play mode

1. Sign in, start Tutorial or a training scene, and generate several real NPC responses.
   Confirm one emotion-history entry per NPC response. Keyboard facial tests alone do
   not create dialogue records and should not save emotions.
2. Finish the conversation. Require the console confirmation containing the UUID.
   Check all four tables, including performance_metric.
3. Start another session and exit midway. Verify status is Incomplete with collected turns.
4. Exit before starting. Verify the session count does not increase.
5. Open/close face zoom repeatedly during a conversation. Check history continues.
6. Disconnect the network before ending a session. Confirm an operation/HTTP error and
   no success message. Reconnect and invoke RetryPendingSaves while still signed in.
7. Retry again. Verify session/turn/emotion counts and the final status do not change.
8. Test all six emotions in real turns, including repeated labels on different turns.
   Inspect live facial expressions, voice/lip-sync, mood environment and face zoom.
9. Force a database failure in a test project (e.g. revoke function execution), then
   verify no partial new session appears. Restore access and retry the retained payload.
10. Test scene navigation during a delayed save. The request should complete independently
    of the scene UI. Test on the headset as well as in the Editor.

Use the confirmed UUID below in the SQL Editor:

```sql
SELECT s.session_id, s.status, t.turn_id, t.turn_sequence,
       t.user_transcript, t.ai_response_text,
       e.npc_name, e.previous_emotion, e.current_emotion,
       e.emotion_intensity, e.recorded_at
FROM public.session s
JOIN public.dialogue_turn t ON t.session_id = s.session_id
LEFT JOIN public.npc_emotion_log e ON e.turn_id = t.turn_id
WHERE s.session_id = 'REPLACE-WITH-SESSION-UUID'::uuid
ORDER BY t.turn_sequence, e.recorded_at;

SELECT e.current_emotion, count(*) AS occurrences,
       round(100.0 * count(*) / sum(count(*)) OVER (), 2) AS distribution_percent,
       avg(e.emotion_intensity) AS average_intensity
FROM public.npc_emotion_log e
JOIN public.dialogue_turn t ON t.turn_id = e.turn_id
WHERE t.session_id = 'REPLACE-WITH-SESSION-UUID'::uuid
GROUP BY e.current_emotion;
```

Existing normalized fetch methods now include dialogue_turn.npc_emotion_log and use
the signed-in access token when available. Dashboard UI has not been edited.
