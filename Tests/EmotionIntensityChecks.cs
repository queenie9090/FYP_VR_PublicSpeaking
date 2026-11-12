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
