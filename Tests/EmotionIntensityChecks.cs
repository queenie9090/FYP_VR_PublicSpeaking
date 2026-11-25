using System;
class EmotionIntensityChecks
{
    static void Check(bool value, string label)
    {
        if (!value) throw new Exception(label);
        Console.WriteLine("PASS " + label);
    }
    static void Main()
    {
        Check(EmotionIntensityClassifier.Classify("NEUTRAL", "extremely furious!", "") == 0, "neutral");
        string[] labels = { "HAPPY", "SAD", "ANGRY", "NERVOUS", "THINKING" };
        foreach (var label in labels)
        {
            int score = EmotionIntensityClassifier.Classify(label, null, null);
            Check(score >= 1 && score <= 100, label + " range");
        }
        int baseline = EmotionIntensityClassifier.Classify("HAPPY", "", "Good to hear.");
        int strong = EmotionIntensityClassifier.Classify("HAPPY", "", "I am extremely thrilled!");
        Check(strong > baseline, "strong and contextual cues");
        Check(EmotionIntensityClassifier.Classify("HAPPY", "", "slightly happy") <
            EmotionIntensityClassifier.Classify("HAPPY", "", "happy"), "softener");
        Check(EmotionIntensityClassifier.Classify("angry", null, "furious") ==
            EmotionIntensityClassifier.Classify("ANGRY", null, "furious"), "case insensitive");
        Check(EmotionIntensityClassifier.Classify("INVALID", "", "") == 0, "unsupported label");
        Check(EmotionIntensityClassifier.Classify("ANGRY", "You are incompetent.", "That was disrespectful.", true) == 83, "Businessman personal insult and boundary");
        Check(EmotionIntensityClassifier.Classify("ANGRY", "You are incompetent.", "I am furious.", true) == 100, "Businessman justified maximum");
        Check(EmotionIntensityClassifier.Classify("SAD", "My father died.", "That is painful.", true) == 83, "Businessman loss and sympathetic response");
        Check(EmotionIntensityClassifier.Classify("ANGRY", "This tool is useless.", "I disagree.", true) == 20, "ordinary disagreement");
        Check(EmotionIntensityClassifier.Classify("ANGRY", "You are not incompetent.", "It wasn't disrespectful.", true) == 20, "negated cues");
        Check(EmotionIntensityClassifier.Classify("ANGRY", "You are incompetent.", "That was disrespectful.") == 20, "other NPCs retain original scoring");
        foreach (var sample in new[] {
            ("HAPPY", "We succeeded.", "I am impressed.", "I am thrilled."),
            ("NERVOUS", "The deadline is tomorrow.", "I feel apprehensive.", "I am panicking."),
            ("THINKING", "Compare the alternatives.", "I am weighing the options.", "I am pondering the options.")
        })
        {
            var (label, context, reply, strongReply) = sample;
            Check(EmotionIntensityClassifier.Classify(label, context, reply, true) == 83, label + " contextual Businessman reaction");
            Check(EmotionIntensityClassifier.Classify(label, context, strongReply, true) == 100, label + " strong Businessman reaction");
            Check(EmotionIntensityClassifier.Classify(label, context, reply) == 20, label + " default profile");
            Check(EmotionIntensityClassifier.Classify(label, "", "", true) == 20, label + " no evidence");
        }
        Check(EmotionIntensityClassifier.Classify("NEUTRAL", "urgent", "thrilled", true) == 0, "sensitive neutral stays zero");
        Check(!EmotionIntensityClassifier.HasConversationTrigger(null), "no trigger on silence");
        Check(!EmotionIntensityClassifier.HasConversationTrigger("um... uh"), "filler is not context");
        Check(EmotionIntensityClassifier.HasConversationTrigger("yes"), "short response is context");
        Check(EmotionIntensityClassifier.HasConversationTrigger("I am worried"), "context trigger");
        Check(EmotionIntensityClassifier.NeedsReassessment("INVALID", "HAPPY", "I feel good"), "invalid label reassessed");
        Check(EmotionIntensityClassifier.NeedsReassessment("SAD", "HAPPY", ""), "silence change reassessed");
        Check(!EmotionIntensityClassifier.NeedsReassessment("HAPPY", "HAPPY", null), "same emotion accepted");
        Check(!EmotionIntensityClassifier.NeedsReassessment("SAD", "HAPPY", "I lost my job"), "contextual change accepted");
        foreach (string shortAnswer in new[] { "Yes", "No", "Absolutely", "I agree", "Thank you" })
            Check(!EmotionIntensityClassifier.NeedsReassessment("HAPPY", "SAD", shortAnswer), "short answer " + shortAnswer);
        Check(EmotionIntensityClassifier.Classify("ANGRY", "", "not furious") < EmotionIntensityClassifier.Classify("ANGRY", "", "furious"), "negation");
        Check(EmotionIntensityClassifier.Classify("NERVOUS", "", "no longer nervous") == 20, "no longer");
        Check(EmotionIntensityClassifier.Classify("NERVOUS", "", "never worried") == 20, "never");
        Check(EmotionIntensityClassifier.Classify("HAPPY", "", "She said \"thrilled\"") == 20, "quoted cue");
        Check(EmotionIntensityClassifier.Classify("HAPPY", "", "She said 'thrilled'") == 20, "single-quoted cue");
        Check(EmotionIntensityClassifier.Classify("ANGRY", "", "my boss is furious") == 20, "other person's emotion");
        Check(EmotionIntensityClassifier.Classify("HAPPY", "", "not happy but delighted") > 20, "mixed cues");
        Check(EmotionIntensityClassifier.Classify("HAPPY", "", "extremely thrilled thrilled incredibly delighted!") <= 100, "upper bound");
        Check(EmotionIntensityClassifier.Classify("SAD", "", "slightly mildly little") >= 1, "lower bound");
        Check(EmotionIntensityClassifier.Classify("SAD", "", "devastated") ==
            EmotionIntensityClassifier.Classify("SAD", "", "devastated"), "deterministic");
    }
}
