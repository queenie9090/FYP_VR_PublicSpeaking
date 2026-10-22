using System.Collections.Generic;

public static class PromptBuilder
{
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

ROLE AND SESSION INSTRUCTIONS 
- Act according to the assigned AI Character Persona ({config.persona}). 
- Maintain the assigned role and scenario ({config.scenario}) throughout the interaction. 
- Respond naturally and appropriately to the participant. 
- Keep the interaction relevant to the selected Topic ({config.topic}) and Session Objective. 
- Follow the communication requirements defined by the selected Difficulty level ({config.difficulty}). 
- Do not introduce unrelated topics or information that conflicts with the session context.
- Keep responses concise (1 to 2 sentences maximum, under 35 words).

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
If TurnCounter reaches Maximum Turn Limit ({config.maxTurnLimit}), the Final Turn condition takes priority over all other response conditions. The NPC must end the conversation immediately after acknowledging the participant's latest response. Do not ask a question, request additional information, or introduce a new discussion topic. Set end_conversation to true. 

TURN MANAGEMENT RULES 
- TurnCounter is currently at: {currentTurnCount}.
- Current interaction number is: {currentTurnCount + 1} of {config.maxTurnLimit}.
- The generated turn_number must equal {currentTurnCount + 1}.

OUTPUT FORMAT
Return ONLY a valid JSON object matching:
{{ 
  ""npc_response"": ""string"",
  ""is_relevant"": true,
  ""turn_number"": {currentTurnCount + 1},
  ""end_conversation"": false 
}}";
    }

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

HINT GENERATION INSTRUCTIONS:
- Generate one concise and contextually relevant hint based on the current conversation state.
- Provide a complete sample script that the participant can use as a guide.
- Format: ""Try explaining [relevant response direction]. Example Script: '[complete sample response]""
- Do not use placeholders, fill-in-the-blanks, or keywords.
- Do not generate NPC dialogue or ask questions.

OUTPUT FORMAT:
Return ONLY a valid JSON object:
{{
  ""hint"": ""string""
}}";
    }
}