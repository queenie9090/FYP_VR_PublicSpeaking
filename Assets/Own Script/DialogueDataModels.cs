using System;
using System.Collections.Generic;

[Serializable]
public class ScenarioConfig
{
    public string scenario = "Classroom"; // "Hall", "Normal Conversation", "Classroom", "Interview"
    public string participantRole = "Student";
    public string topic = "General Presentation";
    public string difficulty = "Medium";
    public string persona = "Teacher";
    public string sessionObjective = "Engage effectively with the evaluator.";
    public int maxTurnLimit = 5;
    public string sessionStartSignal = "Session Start";
}

[Serializable]
public class LLMResponse
{
    public string npc_response;
    public bool is_relevant;
    public int turn_number;
    public bool end_conversation;
    public string hint; // Optional hint output if GenerationType = "Hint"
}

[Serializable]
public class DialogueTurn
{
    public string speaker;        // "User" or "NPC"
    public string dialogue;       // Speech transcript
    public float speechDuration;  // Active speaking duration (seconds)
    public float silenceDuration; // Silence duration before speaking (seconds)
    public int fillerWordCount;   // Number of filler words detected in turn
}

[Serializable]
public class SessionResultPayload
{
    public string user_id;           // Foreign key to auth.users.id
    public string scenario_name;     // "Tutorial" or selected scenario name
    public string input_mode;        // "Hands-Free" or "Controller"
    public string topic;
    public string difficulty;

    // Performance Metrics
    public int total_turns;
    public int relevant_turns;
    public float relevance_score;    // 0-100%
    public float average_wpm;        // Words Per Minute
    public float total_silence_sec;  // Cumulative silence in seconds
    public float eye_contact_score;  // 0-100%
    public int filler_word_count;    // Total filler words
    public float fwpm;               // Filler Words Per Minute
    public float speech_clarity_score;// 0-100
    public int expressive_gesture_count;
    public float gestures_per_minute; // GPM
    public float overall_performance_score; // Weighted 0-100 score

    public string performance_rating; // "Excellent" / "Satisfactory" / "Needs Improvement"
    public string conversation_log;   // JSON stringified history
}