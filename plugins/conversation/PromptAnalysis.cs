using System;

namespace FabioSoft.Nucleus.Plugins.Conversation;

public static class PromptAnalysis
{
    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return Math.Max(1, text.Length / 4);
    }
}
