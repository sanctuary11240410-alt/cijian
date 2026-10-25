using System.Text.Json.Serialization;

namespace WordCoach.Core;

public sealed class VocabularyWord
{
    public string Id { get; set; } = "";
    public string LemmaId { get; set; } = "";
    public string ScopeId { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
    public string Term { get; set; } = "";
    public string Phonetic { get; set; } = "";
    public string Meaning { get; set; } = "";
    public List<string> Phrases { get; set; } = [];
    public string Example { get; set; } = "";
    public string SourceExcerpt { get; set; } = "";
    public int ExamFrequency { get; set; }
    public int NecessityScore { get; set; } // Retained for old vocabulary files; never used as unfamiliarity.
    public List<string> Reasons { get; set; } = [];
    public string Source { get; set; } = "";
    public bool IsFoundation { get; set; }
    public bool RecommendationEligible { get; set; }
    public string RecommendationReason { get; set; } = "";
    public int RecommendationPriority { get; set; }
    public string Coverage { get; set; } = "教材覆盖待核对";
    public string EntryKind { get; set; } = "Word";
    public List<string> EvidenceSources { get; set; } = [];
}

public sealed partial class WordProgress
{
    public string WordId { get; set; } = "";
    public string Status { get; set; } = "New";
    public bool PermanentlySkipped { get; set; }
    public bool Introduced { get; set; }
    public bool IsSchoolWord { get; set; }
    public string SourceTag { get; set; } = "";
    public int IntervalDays { get; set; }
    public int Lapses { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<ReviewRecord> History { get; set; } = [];
    public bool InPool { get; set; }
    public int MemoryStep { get; set; }
    public DateTimeOffset? NextReviewAt { get; set; }
    public DateOnly? RelearnDate { get; set; }
    public DateTimeOffset? LastLessonAt { get; set; }
    public DateOnly? LastIntervalAdvance { get; set; }
    public string Note { get; set; } = "";
    public string LastReason { get; set; } = "";
    public DateOnly? AddedOn { get; set; }
    public MemoryTrack Spelling { get; set; } = new();
}

public sealed class MemoryTrack
{
    public int MemoryStep { get; set; }
    public int IntervalDays { get; set; }
    public int Lapses { get; set; }
    public DateTimeOffset? NextReviewAt { get; set; }
    public DateOnly? LastIntervalAdvance { get; set; }
}

public sealed class ReviewRecord
{
    public DateOnly Date { get; set; }
    public DateTimeOffset? At { get; set; }
    public string Rating { get; set; } = "";
    public bool UsedHint { get; set; }
    public bool SpellingCorrect { get; set; }
    public int NextIntervalDays { get; set; }
    public bool IntervalAdvanced { get; set; }
    public string Mode { get; set; } = "";
    public string Target { get; set; } = "Meaning";
    public double DelayDays { get; set; }
}

public sealed class PlanTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WordId { get; set; } = "";
    public string Kind { get; set; } = "New";
    public string State { get; set; } = "Pending";
    public DateTimeOffset? CompletedAt { get; set; }
    public string Reason { get; set; } = "";
    public string Result { get; set; } = "";
    public DateOnly? CarriedFrom { get; set; }
    public int EstimateSeconds { get; set; } = 120;
    public DateTimeOffset? DueAt { get; set; }
    public double ActualSeconds { get; set; }
}

public sealed class DayPlan
{
    public DateOnly Date { get; set; }
    public List<string> NewWordIds { get; set; } = [];
    public List<string> OriginalWordIds { get; set; } = [];
    public bool Completed { get; set; } // User closed the day, distinct from GoalMet.
    public bool StoryCompleted { get; set; }
    public List<string> Changes { get; set; } = [];
    public List<PlanTask> Tasks { get; set; } = [];
    public int NewTarget { get; set; }
    public int OriginalNewTarget { get; set; } = -1;
    public string TargetReason { get; set; } = "";
    public int ReservedMinutes { get; set; }
    public int MinutesBudget { get; set; }
    public bool Opened { get; set; }
    public bool ClosedGoalMet { get; set; }
    public string CloseSummary { get; set; } = "";
    public bool Started { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public int AdditionalMinutes { get; set; }
    public int BatchCount { get; set; }
}

public sealed class WeekPlan
{
    public DateOnly Monday { get; set; }
    public int WeeklyGoal { get; set; }
    public int ReserveSlots { get; set; }
    public List<string> PriorityWordIds { get; set; } = [];
    public Dictionary<string, DayPlan> Days { get; set; } = [];
}

public sealed class DailySession
{
    public DateOnly Date { get; set; }
    public List<string> LearnedWordIds { get; set; } = [];
    public List<string> RelearnedWordIds { get; set; } = [];
    public List<string> ConsolidatedWordIds { get; set; } = [];
    public List<string> ReviewedWordIds { get; set; } = [];
    public int IndependentCorrect { get; set; }
    public int WeakCount { get; set; }
    public int SchoolWordsAdded { get; set; }
    public int ActiveSeconds { get; set; }
    public string Feeling { get; set; } = "";
    public List<DurationSample> DurationSamples { get; set; } = [];
}

public sealed class DurationSample
{
    public string WordId { get; set; } = "";
    public string Kind { get; set; } = "";
    public double Seconds { get; set; }
    public DateTimeOffset At { get; set; }
}

public sealed class ActivityEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset At { get; set; }
    public string WordId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Result { get; set; } = "";
    public bool Independent { get; set; }
    public bool IntervalAdvanced { get; set; }
    public DateTimeOffset? NextAt { get; set; }
}

public sealed class CandidateFeedback
{
    public string WordId { get; set; } = "";
    public string Band { get; set; } = "";
    public bool WasUnknown { get; set; }
}

public sealed partial class UserState
{
    public int SchemaVersion { get; set; } = 1;
    public int WeeklyNewGoal { get; set; } = 30;
    public int DailyMinutes { get; set; } = 20;
    public int DailyNewLimit { get; set; } = 5;
    public int SundayNewLimit { get; set; }
    public bool AdvancedQuickCheck { get; set; } = true;
    public int DiagnosticTarget { get; set; } = 120;
    public Dictionary<string, WordProgress> Progress { get; set; } = [];
    public WeekPlan? CurrentWeek { get; set; }
    public Dictionary<string, DailySession> Sessions { get; set; } = [];
    public List<VocabularyWord> SchoolWords { get; set; } = [];
    public List<VocabularyWord> ImportedWords { get; set; } = [];
    public List<string> NextWeekPriority { get; set; } = [];
    public List<SavedStory> Stories { get; set; } = [];
    public Dictionary<string, WeekPlan> Weeks { get; set; } = [];
    public List<ActivityEvent> Events { get; set; } = [];
    public List<CandidateFeedback> CandidateFeedback { get; set; } = [];
    public bool MigrationCompleted { get; set; }
    public int SchedulerVersion { get; set; }
}

public sealed class SavedStory
{
    public DateOnly Date { get; set; }
    public List<string> WordIds { get; set; } = [];
    public string Style { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed record StudyItem(VocabularyWord Word, PlanTask Task);
public sealed record CandidateEvidence(double Score, string Band, string Reason);
public sealed record DaySummary(int NewDone, int NewTarget, int RelearnDone, int RelearnTotal,
    int ConsolidateDone, int ConsolidateTotal, int ReviewDone, int ReviewTotal, int Skipped,
    int Pending, int EstimatedMinutes, bool GoalMet);
