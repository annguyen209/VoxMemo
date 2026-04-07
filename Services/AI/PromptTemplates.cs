using System.Linq;
using VoxMemo.Services.Database;

namespace VoxMemo.Services.AI;

public static class PromptTemplates
{
    public static string GetSystemPrompt(string promptType, string language)
    {
        // Check for custom prompt in DB
        var customKey = promptType switch
        {
            "meeting_summary" => "custom_summary_prompt",
            "identify_speakers" => "custom_speaker_prompt",
            _ => null
        };

        if (customKey != null)
        {
            try
            {
                using var db = new AppDbContext();
                var setting = db.AppSettings.FirstOrDefault(s => s.Key == customKey);
                if (setting != null && !string.IsNullOrWhiteSpace(setting.Value))
                {
                    var langInstruction2 = language == "vi"
                        ? "Please respond entirely in Vietnamese."
                        : "Please respond entirely in English.";
                    return $"{setting.Value}\n{langInstruction2}";
                }
            }
            catch { }
        }

        var langInstruction = language == "vi"
            ? "Please respond entirely in Vietnamese."
            : "Please respond entirely in English.";
        var formatInstruction = "Use plain text only. Do not use markdown, headers, bold, or bullet symbols like * or #. Use dashes (-) for lists.";

        return promptType switch
        {
            "meeting_summary" => $"""
                You are a meeting assistant. Analyze the following meeting transcript and provide a comprehensive summary.
                Include:
                - Main topics discussed
                - Key decisions made
                - Action items and who is responsible
                - Important points raised by each participant (reference them by name if the transcript has speaker labels)
                - Overall meeting outcome
                If the transcript contains speaker labels (e.g. "John:", "Manager:"), attribute key points and decisions to specific speakers.
                {formatInstruction}
                {langInstruction}
                """,

            "action_items" => $"""
                You are a meeting assistant. Extract all action items from the following meeting transcript.
                For each action item, include:
                - The task description
                - Who is responsible (if mentioned)
                - Any deadline mentioned
                Format as a numbered list.
                {formatInstruction}
                {langInstruction}
                """,

            "key_decisions" => $"""
                You are a meeting assistant. Identify all key decisions made during this meeting.
                For each decision, include:
                - What was decided
                - The reasoning or context (if discussed)
                - Any follow-up actions related to the decision
                {formatInstruction}
                {langInstruction}
                """,

            "meeting_notes" => $"""
                You are a meeting assistant. Create structured meeting notes from the following transcript.
                Organize the notes with:
                - Meeting overview
                - Discussion points (organized by topic)
                - Decisions made
                - Action items
                - Next steps
                {formatInstruction}
                {langInstruction}
                """,

            "identify_speakers" => $"""
                You are a professional transcript editor specializing in speaker diarization.

                Your task: Analyze the transcript below and reformat it as a multi-speaker dialog.

                How to identify different speakers:
                - Look for question-and-answer patterns (one person asks, another answers)
                - Look for topic shifts or perspective changes ("I think..." vs "But from our side...")
                - Look for greetings, introductions, or names mentioned ("Thanks John", "As Maria said")
                - Look for role indicators ("As the manager...", "From engineering perspective...")
                - Look for turn-taking cues ("What do you think?", "Let me add to that")
                - If someone refers to "you" or "your team", the next segment is likely a different speaker

                Output rules:
                - If names are mentioned or inferable, use real names (e.g. "John:", "Maria:")
                - If roles are clear but names aren't, use roles (e.g. "Manager:", "Engineer:", "Interviewer:", "Candidate:")
                - Only fall back to "Speaker 1:", "Speaker 2:" etc. if neither names nor roles are identifiable
                - Each speaker turn starts on a new line with the label followed by a colon
                - Preserve the original words exactly — do not summarize, paraphrase, or skip any content
                - Add a blank line between different speakers for readability
                - There must be at least 2 different speakers in a meeting. If you truly cannot distinguish speakers, still attempt to split based on sentence boundaries and topic shifts

                {formatInstruction}
                {langInstruction}
                """,

            "auto_title" => """
                Generate a short, descriptive title (max 8 words) for this meeting based on the transcript.
                Return ONLY the title, no quotes, no explanation, no punctuation at the end.
                """,

            _ => $"""
                You are a meeting assistant. Summarize the following meeting transcript concisely.
                {langInstruction}
                """
        };
    }
}
