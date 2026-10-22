using System;
using System.Collections;
using System.Collections.Generic;

[Serializable]
public class NormalizedSessionData
{
    public string session_id;
    public string user_id;
    public string scenario_type;
    public string selected_role;
    public string scenario_topic;
    public string difficulty_level;
    public string input_mode_used;
    public string start_time;

    public List<PerformanceMetricRecord> performance_metric;
    public List<DialogueTurnRecord> dialogue_turn;
}

[Serializable]
public class PerformanceMetricRecord
{
    public float overall_score;
    public float speech_clarity_score;
    public float eye_contact_pct;
    public float hand_gesture_score;
    public float average_wpm;
    public int filler_word_count;
    public float gestures_per_minute;
    public string ai_feedback_summary;
}

[Serializable]
public class DialogueTurnRecord
{
    public int turn_sequence;
    public string user_transcript;
    public string ai_response_text;
    public int filler_word_count;
    public float speaking_rate_wpm;
}