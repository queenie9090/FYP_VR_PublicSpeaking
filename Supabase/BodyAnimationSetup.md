# Body Animation Realism integration

## Implementation and limits

NPCBodyAnimation is one optional component per NPC, not a new session manager.
It selects existing configured Animator states on one dedicated masked layer.
The report's algorithm is implemented using the supplied requirements. The full
report sections and Table 4.12 were not attached in this task.

Implemented priorities are supported environment reaction, interviewer activity
with exact emotion preference, generic activity, emotion-specific idle, scene
appropriate ambient activity, and neutral idle/base animation fallback.

Activity is wired from InteractionManager (LISTENING, THINKING, IDLE) and
SpeechInteractionController (SPEAKING only when the audio starts, then IDLE).
Multi-NPC selection routes facial emotion to the selected NPC's available controller.
IsTalking is written only when that bool parameter exists.

Rapid context changes select a new state without starting competing coroutines.
Unchanged states do not restart. Multiple equally suitable states avoid immediate
repetition. One-shot completion returns to idle/base posture during a variation
interval, then the current activity can choose another gesture.

Face zoom neither references nor restarts the body component.
MoodAdaptiveEnvironment relinquishes its optional animation layer when the body
selector owns that same layer. Its light and ambience effects continue.

No Animator Controllers, Avatar Masks or scene components were created/rewired by
this change. Complete the Inspector steps below before expecting body gestures.
Rendering, actual rig compatibility and Supabase writes remain unverified.

## Inspected assets

Existing FBX assets in Assets/Own Animation Clip include SittingTalking,
Sitting Idle, Agreeing, Sitting And Pointing, Sitting Clap, Sitting Clap_2,
Sitting_SlowClap, Cheering While Sitting and SittingVictory.

Existing states include SitFemale's SittingTalk, sit idle and sitslowclap;
SitMale's sit idle, cheersitting and sitting_clap; HallwayIdle's Agreeing,
greeting and standidle2. Some controllers use Start/End/Talk parameters.
The BusinessMan_SittingIdle controller has a Take1 state and no parameters.
Do not assume a shared IsTalking parameter.

Businessman, Business girl, SittingTalking, Agreeing and Cheering While Sitting
import metadata declares Humanoid. Validate their Avatar Configuration and previews
in Unity before retargeting. This metadata check does not prove the resulting pose.

No verified UsePhone, Selfie, Drink or dedicated emotion-specific Thinking/Nervous
body clips were found. Phone/coffee model assets are not animation clips. Missing
actions must use the neutral idle/listening fallback until appropriate clips and
props are manually supplied. Do not assign cheering/dance as ordinary angry gestures.

## Inspector setup for one interviewer

1. Locate the Businessman root containing Animator and NPCEmotionController in
   TutorialScene. Add NPCBodyAnimation to this root. NPCPersona can be on a child;
   conversation and speech scripts resolve the body component through its parents.
   Do not add another NPC model.
2. Assign Animator and the exact NPCEmotionController used by InteractionManager.
   Choose INTERVIEWER. Enter a stable, unique NPC ID such as your project's assigned
   character identifier. This is a text identity, not an existing database NPC UUID.
   Keep the same identifier for the same character across scenes.
3. In the existing Animator Controller, add an additional Override layer with a name
   you choose, for example BodyGestures. Its default weight must be zero.
   Set Body Animation's Layer Name to that exact name.
4. Create and assign an Avatar Mask for the layer. Enable appropriate torso/arm/hand
   bones. Exclude root, legs, neck/head, jaw and eyes. For Generic rigs use verified
   transform paths. Ensure clips have no facial/blendshape curves. Preview shoulders
   and torso carefully because they can indirectly move the head.
5. Disable Apply Root Motion on this NPC. In the clip import settings bake root
   rotation/position into pose as appropriate. Check the NPC stays at its chair.
   For the conversation NPC use Animator Culling Mode Always Animate so the
   close-up view keeps updating. Check the performance impact on the headset.
6. Add states on this layer using the existing compatible clips. Use an actual
   looping idle state as its default. Keep Write Defaults consistent with your
   controller. Avoid automatic transitions/Any State rules on the new layer;
   this component owns its transitions.
7. Add Action entries. Each State is the exact full path, e.g.
   BodyGestures.SittingTalk only after creating that state. The script checks
   HasState at startup and skips unknown paths.

Suggested initial bindings:

| Activity | Emotion filter | Motion | One Shot |
|---|---|---|---|
| IDLE | empty | Existing sitting idle | false; enable clip looping |
| LISTENING | empty | Existing sitting idle or subtle Agreeing | true for Agreeing |
| THINKING | empty | Existing sitting idle fallback | false |
| SPEAKING | empty | Existing SittingTalking | true if non-looping |

Add several suitable SPEAKING states for variation. To specialize, duplicate a
binding with Emotion HAPPY/SAD/ANGRY/NERVOUS/THINKING/NEUTRAL and a verified gesture.
An empty emotion filter matches all six and provides the fallback.
Use relaxed/open clips for HAPPY, reserved/slower clips for SAD, controlled
gestures for ANGRY, subtle restless clips for NERVOUS and attentive clips for THINKING.
Tune state speeds in the Animator; the script does not change global Animator speed.
No emotion-specific motion is claimed until that motion is supplied and previewed.

Blend Seconds defaults to 0.5, Variation Interval to 8, Layer Weight to 0.8.
Existing emotion intensity slightly scales the masked layer weight.
Do not assign a second animation driver to this layer.

## Ambient NPCs and events

Add the same component to each ambient NPC, choose AMBIENT, and give each NPC a
stable unique ID. Use a separate compatible masked layer on its existing Animator.
Real scene names map Hall to HALL, Classroom to CLASSROOM, Cafeteria to KOPITIAM,
and all others to OTHER.

- HALL supports idle phone use when configured. Cheer/Clap require an explicit
  environment-event binding; ordinary idle does not spontaneously trigger them.
- CLASSROOM supports UsePhone, Talk and Selfie.
- KOPITIAM supports Drink, Talk and UsePhone.
- OTHER falls back to IDLE unless an explicit supported environmental event applies.

Under ordinary IDLE, configured idle and UsePhone states can alternate at the
variation interval. Talk, Selfie and Drink require explicit activity context.
Call SetAmbientActivity("Talk"), SetAmbientActivity("Selfie"), or
SetAmbientActivity("Drink") from existing scene interactions, then
SetAmbientActivity("IDLE") when that activity ends. Unsupported scene/action
combinations fall back to idle. This does not create social gameplay or attach props.

To handle an existing environmental event, configure a non-looping action with an
Environment Event string and call ReactToEnvironment with that exact string.
For a test, a button can call ReactToEnvironment("Applause") after an Applause
binding to a clap clip has been configured. No event broadcaster is created.
Repeated requests for the same active event do not restart it. On completion,
selection resumes using the latest underlying activity/emotion.

## Collection and Supabase

The existing NPCEmotionSessionManager collects body events only between
StartNewSession and result snapshotting. Ambient playback outside that interval is
not recorded. Events are added only after the requested Animator state contributes
an actual animation clip; continuous idle loops do not create more rows.

The save snapshot preserves completed/incomplete status and includes body events.
Each event UUID is generated once. Existing retained-payload retries reuse it.
The current session UUID and dialogue UUIDs are assigned by the existing save
pipeline. Interviewer events use the conversation turn sequence to resolve turn_id.
Listening before a transcript or an aborted pending turn may have a null turn_id.
Ambient events always have null turn_id. All saved events have a real parent session.

Apply Supabase/save_training_session.sql first if not already installed, then run
Supabase/body_animation_events.sql in the SQL Editor.

The migration adds body_animation_event, an index on dialogue session/turn IDs,
a composite foreign key preventing cross-session turn links, owner-only SELECT
permissions, and save_training_session_v2. It does not delete existing records.
NPC IDs are configured text identifiers because this schema has no NPC entity.
The new function authenticates ownership and saves all records in one transaction.
Requests containing body events use v2; without the migration they fail visibly
and remain available for retry instead of silently dropping the events.
Sessions without body events continue using the existing function.

No service-role key is used. Existing broader policies on older tables and educator
access are outside this change; the new body-event policy allows the session owner.
No dashboard UI is modified.

## Play Mode and headset tests

1. Configure and preview the interviewer states. Start a signed-in session.
   Confirm LISTENING while recording, THINKING during the AI/TTS wait, SPEAKING
   during actual NPC audio, and IDLE when the session stops.
2. Test each of the six emotions with suitable mapped clips. Confirm generic
   speaking/listening fallback when an emotion-specific state is absent.
3. Configure Hall clap/cheer event bindings and a UsePhone idle binding.
   Confirm group reactions require the event and return to normal afterwards.
4. Test Classroom Talk/Selfie/UsePhone and Cafeteria Drink/Talk/UsePhone with
   manually supplied compatible clips and props. These cannot be visually tested
   with the current unverified/missing assets.
5. Trigger a reaction during speaking. Change activity while it plays. Confirm
   the supported reaction takes priority then returns to the latest activity.
6. Observe face, eye movement, blinking, lip-sync and face zoom during transitions.
   Check clipping, head framing, seated position and comfort in the headset.
7. Leave idle looping and open/close face zoom several times. Check event count
   stays stable until a meaningful new action starts.
8. Finish a session and inspect the returned session UUID in the Console.
   Run the query below. Repeat with Incomplete and with exit-before-start.
9. Disconnect the network at save time, reconnect and invoke
   SupabaseManager.RetryPendingSaves(). Verify counts do not increase on another
   retry. Pending data is memory-only and is lost on application shutdown.
10. Confirm no red Console errors or missing-state/rig warnings remain after setup.

```sql
SELECT b.*, s.status, t.turn_sequence
FROM public.body_animation_event b
JOIN public.session s ON s.session_id = b.session_id
LEFT JOIN public.dialogue_turn t ON t.turn_id = b.turn_id
WHERE b.session_id = 'REPLACE-WITH-CONFIRMED-SESSION-UUID'::uuid
ORDER BY b.timestamp;

-- This must return zero rows.
SELECT b.animation_event_id
FROM public.body_animation_event b
JOIN public.dialogue_turn t ON t.turn_id = b.turn_id
WHERE t.session_id <> b.session_id;
```

## Verification performed

The actual C# source was compiled with Unity's compiler/project references.
A separate logic-only harness exercised selection priorities, generic emotion
fallback, scene restrictions, repeated-state suppression, one-shot fallback,
continuous-idle deduplication and no collection outside a session.
The harness uses Animator stand-ins and does not verify real blending or rig poses.
A read-only Supabase check returned PGRST205 for body_animation_event before this
migration. No SQL migration or database writes were executed by the assistant.
