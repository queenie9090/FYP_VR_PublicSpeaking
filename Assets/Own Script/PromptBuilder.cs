using System.Collections.Generic;

public static class PromptBuilder
{
    /// <summary>
    /// Builds the system prompt for NPC Dialogue generation based on FYP Report Section 4.6.10 & 4.7.1.
    /// </summary>
    public static string BuildSystemPrompt(ScenarioConfig config, int currentTurnCount)
    {
        string difficultyInstruction = config.difficulty switch
        {
            "Easy" => "Use straightforward questions and provide clear guidance when needed.",
            "Hard" => "Use challenging questions requiring specific explanations, examples, or problem-solving, with minimal guidance.",
            _ => "Use moderately challenging questions and provide limited guidance."
        };

        return $@"
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

RESPONSE CONDITION RULES

Normal Response:
If the participant provides a relevant response, respond naturally according to the assigned role, scenario, topic, and difficulty.

Silence:
If the participant does not respond within the time limit, politely encourage them to speak without making assumptions.

Minimal Response:
If the participant responds with fewer than 3 words, encourage them to elaborate.

Repeated Response:
If the participant repeats previously stated details, acknowledge it and pivot to a new relevant topic aspect.

Off-Topic Response:
If the participant speaks off-topic, gently redirect them back toward the session objective.

Final Turn Rule (HIGHEST PRIORITY):
If TurnCounter reaches Maximum Turn Limit ({config.maxTurnLimit}), acknowledge the user's input, provide a brief closing statement, DO NOT ask any question or follow-up, and set end_conversation to true.

TURN MANAGEMENT
- Current Turn: {currentTurnCount + 1} of {config.maxTurnLimit}.

OUTPUT FORMAT
Return ONLY a valid JSON object matching:
{{
  ""npc_response"": ""string"",
  ""is_relevant"": true,
  ""turn_number"": {currentTurnCount + 1},
  ""end_conversation"": false
}}";
    }

    /// <summary>
    /// ALGORITHM 7: Builds prompt for Hand Gesture Hint Activation (Open Palm > 1.0s).
    /// </summary>
    public static string BuildHintPrompt(ScenarioConfig config, List<DialogueTurn> history)
    {
        string conversationContext = "";
        if (history != null && history.Count > 0)
        {
            foreach (var turn in history)
            {
                conversationContext += $"{turn.speaker}: {turn.dialogue}\n";
            }
        }

        return $@"
You are a communication coach assisting a user in a VR training simulation.

CONTEXT:
- Scenario: {config.scenario}
- Topic: {config.topic}
- Role: {config.participantRole}
- Objective: {config.sessionObjective}

RECENT CONVERSATION HISTORY:
{conversationContext}

TASK:
Provide a concise, helpful 1-sentence hint or suggested response to help the user answer effectively.

OUTPUT FORMAT:
Return ONLY a valid JSON object:
{{
  ""hint"": ""string""
}}";
    }
}