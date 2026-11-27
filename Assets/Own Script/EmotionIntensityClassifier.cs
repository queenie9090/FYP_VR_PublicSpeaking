using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

// Deterministic cue-based expression strength, not a semantic model or confidence score.
public static class EmotionIntensityClassifier
{
    private static readonly Dictionary<string, string[]> StrongCues = new()
    {
        { "HAPPY", new[] { "ecstatic", "overjoyed", "thrilled", "delighted", "wonderful" } },
        { "SAD", new[] { "devastated", "heartbroken", "grieving", "miserable", "hopeless" } },
        { "ANGRY", new[] { "furious", "outraged", "enraged", "livid" } },
        { "NERVOUS", new[] { "terrified", "panicking", "petrified", "dreading" } },
        { "THINKING", new[] { "pondering", "analysing", "analyzing", "complex" } }
    };
    private static readonly Dictionary<string, string[]> MildCues = new()
    {
        { "HAPPY", new[] { "happy", "glad", "pleased", "joyful" } },
        { "SAD", new[] { "sad", "upset", "unhappy", "disappointed" } },
        { "ANGRY", new[] { "angry", "annoyed", "frustrated", "irritated" } },
        { "NERVOUS", new[] { "nervous", "worried", "anxious", "uneasy" } },
        { "THINKING", new[] { "thinking", "considering", "wondering", "unsure" } }
    };
    private static readonly string[] BoundaryCues = { "unacceptable", "insulting", "offensive", "disrespectful", "hostile" };
    private static readonly string[] LossCues = { "hurt", "painful", "saddened", "upsetting" };
    private static readonly string[] HostilityCues = { "pathetic", "worthless", "idiot", "useless", "incompetent" };
    private static readonly string[] BereavementCues = { "died", "bereavement", "funeral", "grief" };
    // Dialogue and participant cues are separate so a topic alone cannot supply both weights.
    private static readonly Dictionary<string, (string[] reply, string[] context)> SensitiveCues = new()
    {
        { "SAD", (LossCues, BereavementCues) },
        { "ANGRY", (BoundaryCues, HostilityCues) },
        { "HAPPY", (new[] { "impressed", "proud", "excellent", "fantastic" },
                    new[] { "succeeded", "promoted", "congratulations", "achieved" }) },
        { "NERVOUS", (new[] { "apprehensive", "unsettled", "tense", "overwhelmed" },
                      new[] { "deadline", "urgent", "unprepared", "risk" }) },
        { "THINKING", (new[] { "weighing", "comparing", "evaluating", "examining" },
                       new[] { "tradeoff", "alternatives", "tradeoffs", "dilemma", "compare" }) }
    };
    private static readonly string[] Amplifiers = { "very", "extremely", "deeply", "really", "incredibly" };
    private static readonly string[] Softeners = { "slightly", "somewhat", "little", "mildly", "bit" };
    private static readonly string[] Negations = { "not", "never", "no", "neither", "hardly", "isn't", "wasn't", "don't", "didn't", "cannot", "can't" };
    private static readonly string[] OtherSubjects = { "he", "she", "they", "someone", "boss", "colleague", "friend", "customer", "others" };
    private static readonly Regex Quoted = new Regex("[\"“][^\"”]*[\"”]|(?<![\\p{L}])'[^']+'(?![\\p{L}])", RegexOptions.Compiled);
    private static readonly Regex Words = new Regex(@"[\p{L}\p{N}']+|[.!?;,]", RegexOptions.Compiled);

    public static bool IsSupported(string emotion)
    {
        string label = (emotion ?? "").Trim().ToUpperInvariant();
        return label == "NEUTRAL" || StrongCues.ContainsKey(label);
    }

    public static bool HasConversationTrigger(string participantText)
    {
        string text = (participantText ?? "").Trim();
        if (text.Length == 0 || text.Equals("[Silence / No Response Provided]", StringComparison.OrdinalIgnoreCase))
            return false;
        foreach (Match token in Words.Matches(text.ToLowerInvariant()))
        {
            string word = token.Value;
            if (word == "um" || word == "uh" || word == "uhm" || word == "er" || word == "hmm") continue;
            if (Regex.IsMatch(word, @"[\p{L}\p{N}]", RegexOptions.CultureInvariant)) return true;
        }
        return false;
    }

    public static bool NeedsReassessment(string emotion, string previousEmotion, string participantText)
    {
        if (!IsSupported(emotion)) return true;
        string current = emotion.Trim().ToUpperInvariant();
        string previous = IsSupported(previousEmotion)
            ? previousEmotion.Trim().ToUpperInvariant() : "NEUTRAL";
        return current != previous && !HasConversationTrigger(participantText);
    }

    public static int Classify(string emotion, string participantText, string npcText, bool sensitive = false)
    {
        string label = (emotion ?? "").Trim().ToUpperInvariant();
        if (label == "NEUTRAL" || !StrongCues.ContainsKey(label)) return 0;
        var (replyCues, contextCues) = sensitive ? SensitiveCues[label] : default;
        int npcStrength = ScoreText(label, npcText, replyCues, sensitive);
        int participantStrength = ScoreText(label, participantText, contextCues, sensitive);
        // The NPC's own expression carries more weight than the participant's context.
        int score = 20 + npcStrength + (sensitive ? participantStrength * 3 / 4 : participantStrength / 2);
        return Math.Max(1, Math.Min(100, score));
    }

    // Hall audiences do not speak: score the participant context only and reject
    // absent/invalid labels or silence instead of inventing a new reaction.
    public static bool TryClassifyHallAudience(string rawEmotion, string participantText, out string emotion, out int intensity)
    {
        emotion = null;
        intensity = 0;
        if (!IsSupported(rawEmotion) || !HasConversationTrigger(participantText)) return false;
        emotion = rawEmotion.Trim().ToUpperInvariant();
        intensity = Classify(emotion, participantText, "", sensitive: true);
        return true;
    }

    private static int ScoreText(string label, string input, string[] extraCues, bool sensitive)
    {
        string text = Quoted.Replace((input ?? "").ToLowerInvariant().Replace('’', '\''), " ");
        var matches = Words.Matches(text);
        var tokens = new List<string>();
        foreach (Match match in matches) tokens.Add(match.Value);
        int best = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            bool strong = Array.IndexOf(StrongCues[label], tokens[i]) >= 0;
            bool mild = Array.IndexOf(MildCues[label], tokens[i]) >= 0;
            bool contextual = extraCues != null && Array.IndexOf(extraCues, tokens[i]) >= 0;
            if (contextual && extraCues == HostilityCues)
            {
                // Only count a personal insult addressed to the listener.
                int j = i - 1;
                while (j >= 0 && (tokens[j] == "a" || tokens[j] == "an" || tokens[j] == "so" ||
                    Array.IndexOf(Amplifiers, tokens[j]) >= 0)) j--;
                contextual = j >= 0 && (tokens[j] == "you're" || tokens[j] == "you" ||
                    (tokens[j] == "are" && j > 0 && tokens[j - 1] == "you"));
            }
            if (!strong && !mild && !contextual) continue;
            bool negated = false;
            bool otherSubject = false;
            int modifier = 0;
            for (int j = i - 1; j >= Math.Max(0, i - 3); j--)
            {
                string word = tokens[j];
                if (word == "." || word == "!" || word == "?" || word == ";" ||
                    word == "," || word == "but" || word == "however") break;
                if (Array.IndexOf(Negations, word) >= 0) negated = true;
                if (Array.IndexOf(OtherSubjects, word) >= 0) otherSubject = true;
                if (Array.IndexOf(Amplifiers, word) >= 0) modifier += 10;
                if (Array.IndexOf(Softeners, word) >= 0) modifier -= 8;
            }
            if (negated || otherSubject) continue;
            int value = (strong ? (sensitive ? 55 : 40) : contextual ? 36 : sensitive ? 28 : 18) + modifier;
            if (i + 1 < tokens.Count && tokens[i + 1] == "!") value += 3;
            best = Math.Max(best, value);
        }
        return Math.Max(0, Math.Min(65, best));
    }
}
