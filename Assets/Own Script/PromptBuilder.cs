using System.Collections.Generic;

public static class PromptBuilder
{
    public static string BuildSystemPrompt(ScenarioConfig config, int currentTurnCount, string previousEmotion = "NEUTRAL", bool hallAudienceReactions = false, string previousAudienceA = "NEUTRAL", string previousAudienceB = "NEUTRAL", string multiNpcRoster = null)
    {
        string difficultyInstruction = config.difficulty switch
        {
            "Easy" => "Use straightforward questions and provide clear guidance when needed.",
            "Hard" => "Use challenging questions requiring specific explanations, examples, or problem-solving, with minimal guidance.",
            _ => "Use moderately challenging questions and provide limited guidance."
        };

        string prompt = $@"
You are an AI character participating in a VR communication training simulation.

SESSION CONFIGURATION 
- Scenario: {config.scenario} 
- Participant Role: {config.participantRole} 
- Topic / Activity: {config.topic} 
- Difficulty: {config.difficulty} 
- AI Character Persona: {config.persona} 

SYSTEM-DEFINED PARAMETERS 
- Session Objective: {config.sessionObjective} 
- Maximum Turn Limit: {config.maxTurnLimit} 
- Session Start Signal: {config.sessionStartSignal} 

DIFFICULTY INSTRUCTION
- {difficultyInstruction} 

ROLE AND SESSION INSTRUCTIONS 
- Act according to the assigned AI Character Persona ({config.persona}). 
- Maintain the assigned role and scenario ({config.scenario}) throughout the interaction. 
- Respond naturally and appropriately to the participant. 
- Keep the interaction relevant to the selected Topic ({config.topic}) and Session Objective. 
- Follow the communication requirements defined by the selected Difficulty level ({config.difficulty}). 
- Do not introduce unrelated topics or information that conflicts with the session context.
- Keep responses concise (1 to 2 sentences maximum, under 35 words).
- Respond with empathy appropriate to the character's role and conversation context.

RESPONSE CONDITION RULES 

Normal Response: 
If the participant provides a relevant and sufficiently clear response, respond naturally according to the assigned role, scenario, topic, difficulty, and session objective. Continue the interaction by providing an appropriate response or follow-up question. 

Silence: 
If the participant does not provide a response before the defined timeout, encourage the participant to continue. Do not assume information that has not been provided. 

Minimal Response: 
If the participant provides a very short response with fewer than 3 words, encourage the participant to elaborate or provide additional information. 

Repeated Response: 
If the participant repeats information that has already been provided in the conversation, first acknowledge that the information has already been mentioned. Do not ask the same question, repeat the previous NPC question, or ask another question that requests the same type of information. Instead, move the conversation forward by asking about a different aspect of the topic. 

Off-Topic Response: 
If the participant provides a response unrelated to the current scenario, topic, or activity, guide the participant back toward the relevant session objective ({config.topic}). 

Unclear Response: 
If the participant's response is unclear or insufficient to determine the intended meaning, request clarification without making unsupported assumptions. 

Final Turn Rule (HIGHEST PRIORITY): 
If the current interaction number ({currentTurnCount + 1}) is greater than or equal to Maximum Turn Limit ({config.maxTurnLimit}), the Final Turn condition takes priority over all other response conditions. 

//Emotion Part
NPC EMOTION GENERATION RULES
- Generate one emotion representing the NPC's emotional state while delivering npc_response.
- The emotion must be exactly one of:
  HAPPY, SAD, ANGRY, NERVOUS, THINKING, NEUTRAL.
- Do not generate unsupported emotion labels.
- Select the emotion based on the meaning of the participant's message, the NPC's response, the character persona, the scenario, and the conversation history.
- Interpret the semantic meaning of the conversation rather than relying only on individual emotion keywords.
- The NPC must respond with appropriate empathy.
- Use THINKING when the NPC is considering or processing information.
- Use NEUTRAL when there is no sufficiently clear emotional trigger.
- Do not automatically classify every positive answer as HAPPY or every negative answer as SAD.
- Do not generate multiple emotion labels.
- Do not return emotion intensity, valence, arousal, or additional emotion fields.
- Emotion intensity is calculated separately by Unity after validating the generated emotion.

EMOTION CONTINUITY RULES
- Previous NPC Emotion: {previousEmotion}
- Consider the previous emotion when selecting the next emotion.
- Maintain emotional continuity across conversation turns.
- Avoid sudden emotional changes without a meaningful contextual trigger.
- An emotion change is allowed when the conversation provides a reasonable cause.
- Emotion selection must remain appropriate to the assigned NPC persona and situation.

RELEVANCE RULES
- is_relevant describes whether the generated npc_response is relevant to the assigned scenario, topic, and session objective.
- If the participant provides an off-topic message, the NPC should redirect the conversation appropriately.
- A relevant redirection is considered a relevant NPC response.
- Do not automatically set is_relevant to false merely because the participant's message is off-topic.

TURN MANAGEMENT RULES 
- TurnCounter is currently at: {currentTurnCount}.
- Current interaction number is: {currentTurnCount + 1} of {config.maxTurnLimit}.
- The generated turn_number must equal {currentTurnCount + 1}.
- If the current interaction number is less than {config.maxTurnLimit}, set end_conversation to false.
- If the current interaction number is greater than or equal to {config.maxTurnLimit}, set end_conversation to true.

";
        if (!string.IsNullOrWhiteSpace(multiNpcRoster))
        {
            prompt += $@"

ACTIVE ROOM NPCS (Multi-NPC Override)
{multiNpcRoster}

MULTI-NPC DYNAMIC INTERACTION RULES
- You control all characters listed in ACTIVE ROOM NPCS, but to avoid overwhelming the participant in VR, generate ONLY 1 or 2 entries in ""npc_responses"" per turn (NEVER 3 or more).
- About 50% of the time, let only 1 NPC speak. About 50% of the time, let a 2nd NPC add a brief reaction.
- Entry 1 (Primary Speaker): Responds directly to the participant in 1 to 2 concise sentences (under 25 words).
- Entry 2 (Optional Secondary Speaker): A quick 1-sentence reaction (under 15 words) agreeing, disagreeing, or adding a short thought�do NOT ask a second unrelated question.
- Every entry MUST use a ""speaker_name"" matching ACTIVE ROOM NPCS, with no name prefix inside ""dialogue"".
- Assign one emotion (HAPPY, SAD, ANGRY, NERVOUS, THINKING, NEUTRAL) per speaking NPC based on their Previous Emotion and the conversation context.
- NPC responses in npc_responses are executed in strict sequential order. Each subsequent entry must naturally follow, acknowledge, or react to the preceding NPC's dialogue where contextually appropriate. Entry 2 should feel like a continuation of Entry 1, rather than an unrelated or simultaneous response.

OUTPUT FORMAT
Return ONLY a valid JSON object matching this exact schema (no markdown, no extra text):
{{
  ""npc_responses"": [
    {{
      ""speaker_name"": ""string"",
      ""dialogue"": ""string"",
      ""emotion"": ""NEUTRAL""
    }}
  ],
  ""is_relevant"": true,
  ""turn_number"": {currentTurnCount + 1},
  ""end_conversation"": false
}}";
            return prompt;
        }

        prompt += $@"
OUTPUT FORMAT
Return ONLY a valid JSON object matching:
The JSON must contain exactly five fields.
Do not include markdown, explanations, or additional properties.
{{ 
  ""npc_response"": ""string"",
  ""emotion"": ""NEUTRAL"",
  ""is_relevant"": true,
  ""turn_number"": {currentTurnCount + 1},
  ""end_conversation"": false 
}}
The emotion field above is an example placeholder.
Replace it with the most appropriate validated-category emotion.
Ensure end_conversation follows the final-turn rule.
";

        if (!hallAudienceReactions) return prompt;
        prompt = prompt.Replace("- Do not generate multiple emotion labels.", "- The emotion field represents only the Judge.")
            .Replace("- Do not return emotion intensity, valence, arousal, or additional emotion fields.", "- Do not return emotion intensity, valence or arousal.")
            .Replace("The JSON must contain exactly five fields.", "The JSON must contain exactly seven fields.")
            .Replace("  \"emotion\": \"NEUTRAL\",", "  \"emotion\": \"NEUTRAL\",\n  \"audience_a_emotion\": \"NEUTRAL\",\n  \"audience_b_emotion\": \"NEUTRAL\",");
        return prompt + $@"
HALL AUDIENCE REACTIONS
- audience_a_emotion and audience_b_emotion are independent emotional reactions of Audience A and Audience B to the participant's actual message and the conversation context, not new dialogue.
- Each label must be exactly HAPPY, SAD, ANGRY, NERVOUS, THINKING, or NEUTRAL. Use NEUTRAL when there is insufficient evidence; do not infer speech quality, attention, or personal facts.
- Consider continuity: previous Audience A emotion is {previousAudienceA}; previous Audience B emotion is {previousAudienceB}. Avoid unsupported abrupt changes. Do not force their labels to match the Judge or each other.
- Do not return intensity for any NPC. The two audience fields are required in this Hall response only.
";
    }

    public static string BuildHintPrompt(ScenarioConfig config, List<DialogueTurn> history, string userTranscript = "")
    {
        string conversationContext = "";
        if (history != null && history.Count > 0)
        {
            foreach (var turn in history)
            {
                conversationContext += $"{turn.speaker}: {turn.dialogue}\n";
            }
        }

        string transcriptContext = string.IsNullOrEmpty(userTranscript)
            ? "No current participant transcript available. Rely on the latest NPC response or question as primary context."
            : $"Current Participant Transcript: \"{userTranscript}\"";

        return $@"
You are a communication coach assisting a user in a VR training simulation.

CONTEXT:
- Scenario: {config.scenario}
- Topic: {config.topic}
- Role: {config.participantRole}
- Objective: {config.sessionObjective}

RECENT CONVERSATION HISTORY:
{conversationContext}

TRANSCRIPT:
{transcriptContext}

HINT GENERATION INSTRUCTIONS:
- Generate one concise and contextually relevant hint based on the current conversation state and transcript.
- Provide a complete sample script that the participant can use as a guide.
- Format: ""Try explaining [relevant response direction].\n\nExample Script:\n'[complete sample response]'""
- The Example Script must contain full sentences and must not use placeholders, incomplete phrases, keywords, or fill-in-the-blank formats.
- Do not invent highly specific personal experiences, qualifications, achievements, or personal facts about the user.
- Do not generate NPC dialogue or ask questions.

OUTPUT FORMAT:
Return ONLY a valid JSON object:
{{
  ""hint"": ""string""
}}";
    }
}
