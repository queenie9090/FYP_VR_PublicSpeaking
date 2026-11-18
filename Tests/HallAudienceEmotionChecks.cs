using System;
class HallAudienceEmotionChecks
{
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static void Main()
    {
        var cfg = new ScenarioConfig();
        string regular = PromptBuilder.BuildSystemPrompt(cfg, 0);
        string hall = PromptBuilder.BuildSystemPrompt(cfg, 0, "NEUTRAL", true, "HAPPY", "SAD");
        Check(!regular.Contains("audience_a_emotion") && regular.Contains("exactly five fields"), "other scenarios retain five-field format");
        Check(hall.Contains("audience_a_emotion") && hall.Contains("audience_b_emotion") && hall.Contains("exactly seven fields") && !hall.Contains("exactly five fields"), "Hall requests independent fields");
        Check(hall.Contains("previous Audience A emotion is HAPPY") && hall.Contains("previous Audience B emotion is SAD"), "independent continuity in prompt");
        Check(!EmotionIntensityClassifier.TryClassifyHallAudience(null,"hello",out _,out _), "missing label retains state");
        Check(!EmotionIntensityClassifier.TryClassifyHallAudience("CONFUSED","hello",out _,out _), "unsupported label retains state");
        Check(!EmotionIntensityClassifier.TryClassifyHallAudience("HAPPY","[Silence / No Response Provided]",out _,out _), "silence retains state");
        Check(EmotionIntensityClassifier.TryClassifyHallAudience(" neutral ","hello",out var neutral,out var zero) && neutral == "NEUTRAL" && zero == 0, "neutral intensity zero");
        foreach (string label in new[]{"HAPPY","SAD","ANGRY","NERVOUS","THINKING"})
            Check(EmotionIntensityClassifier.TryClassifyHallAudience(label,"A meaningful participant response",out var e,out var v) && e == label && v >= 1 && v <= 100, label + " valid intensity");
    }
}
