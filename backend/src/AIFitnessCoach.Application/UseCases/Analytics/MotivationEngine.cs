using AIFitnessCoach.Domain.Entities;

namespace AIFitnessCoach.Application.UseCases.Analytics;

/// <param name="Current">Consecutive days with progress up to today; yesterday still counts until today ends.</param>
public sealed record Streak(int Current, int Longest, bool ActiveToday, DateOnly? LastActiveDay);

public sealed record Achievement(
    string Id,
    string Title,
    string Description,
    bool Unlocked,
    DateOnly? UnlockedOn,
    int Progress,
    int Target);

public sealed record WeeklySummary(
    DateOnly From,
    DateOnly To,
    int ActiveDays,
    int WorkoutsCompleted,
    int WorkoutsPlanned,
    int PreviousWeekWorkoutsCompleted,
    int MealsLogged,
    int CoachQueries,
    int FeedbackSubmitted,
    double? AverageDailySteps,
    string Headline,
    IReadOnlyList<string> Highlights);

public static class ReminderKinds
{
    public const string PendingWorkout = "pending_workout";
    public const string DailyGoal = "daily_goal";
    public const string WeeklySummary = "weekly_summary";
}

/// <summary>Content for a local notification; the app decides the exact local time.</summary>
public sealed record Reminder(string Kind, string Title, string Body, string DeepLink, DateOnly Date);

public sealed record EngagementSummary(
    DateTimeOffset GeneratedAt,
    Streak Streak,
    WeeklySummary WeeklySummary,
    IReadOnlyList<Achievement> Achievements,
    AdherenceReport Adherence,
    IReadOnlyList<Reminder> Reminders);

/// <summary>Streaks, simple achievements, the weekly summary and today's reminders for one user.</summary>
public sealed class MotivationEngine(AdherenceEngine adherenceEngine)
{
    public MotivationEngine() : this(new AdherenceEngine()) { }

    public EngagementSummary Build(UserActivitySnapshot snapshot, DateTimeOffset now, TimeSpan utcOffset)
    {
        var today = AnalyticsCalendar.Day(now, utcOffset);
        var progressDays = ActivityDays.Progress(snapshot, utcOffset);
        var streak = CalculateStreak(progressDays, today);
        var weekly = BuildWeeklySummary(snapshot, today, utcOffset, streak);
        var adherence = adherenceEngine.Calculate(snapshot, today.AddDays(-27), today, utcOffset);
        return new EngagementSummary(
            now,
            streak,
            weekly,
            BuildAchievements(snapshot, progressDays, utcOffset),
            adherence,
            BuildReminders(snapshot, today, utcOffset, streak, weekly, progressDays));
    }

    public static Streak CalculateStreak(IReadOnlySet<DateOnly> progressDays, DateOnly today)
    {
        var days = progressDays.Where(day => day <= today).ToHashSet();
        if (days.Count == 0)
            return new Streak(0, 0, false, null);

        var activeToday = days.Contains(today);
        var cursor = activeToday ? today : today.AddDays(-1);
        var current = 0;
        while (days.Contains(cursor))
        {
            current++;
            cursor = cursor.AddDays(-1);
        }

        var longest = 0;
        var run = 0;
        DateOnly? previous = null;
        foreach (var day in days.Order())
        {
            run = previous is { } last && day.DayNumber == last.DayNumber + 1 ? run + 1 : 1;
            longest = Math.Max(longest, run);
            previous = day;
        }
        return new Streak(current, longest, activeToday, days.Max());
    }

    private static WeeklySummary BuildWeeklySummary(
        UserActivitySnapshot snapshot,
        DateOnly today,
        TimeSpan utcOffset,
        Streak streak)
    {
        var from = today.AddDays(-6);
        bool InWeek(DateOnly day) => day >= from && day <= today;
        bool InPreviousWeek(DateOnly day) => day >= from.AddDays(-7) && day < from;

        var completedDays = snapshot.WorkoutDays
            .Where(day => day.CompletedAt is not null)
            .Select(day => AnalyticsCalendar.Day(day.CompletedAt!.Value, utcOffset))
            .ToArray();
        var workoutsCompleted = completedDays.Count(InWeek);
        var previousWorkouts = completedDays.Count(InPreviousWeek);
        var workoutsPlanned = snapshot.WorkoutDays.Count(day => InWeek(day.PlannedOn));
        var activeDays = ActivityDays.Active(snapshot, utcOffset).Count(InWeek);
        var mealsLogged = CountEvents(snapshot, ProductEventNames.MealLogged, InWeek, utcOffset);
        var coachQueries = CountEvents(snapshot, ProductEventNames.CoachQuery, InWeek, utcOffset);
        var feedback = CountEvents(snapshot, ProductEventNames.WorkoutFeedbackSubmitted, InWeek, utcOffset);
        var steps = snapshot.DailySteps.Where(pair => InWeek(pair.Key)).Select(pair => pair.Value).ToArray();
        double? averageSteps = steps.Length == 0 ? null : Math.Round(steps.Average());

        var highlights = new List<string>();
        if (workoutsCompleted > 0)
        {
            var planned = workoutsPlanned > 0 ? $" de {workoutsPlanned} planificados" : string.Empty;
            highlights.Add($"Completaste {workoutsCompleted} entrenamiento{Plural(workoutsCompleted)}{planned}.");
        }
        if (workoutsCompleted > previousWorkouts && previousWorkouts > 0)
            highlights.Add($"Son {workoutsCompleted - previousWorkouts} más que la semana anterior.");
        if (mealsLogged > 0)
            highlights.Add($"Registraste {mealsLogged} comida{Plural(mealsLogged)}.");
        if (averageSteps is { } stepsAverage)
            highlights.Add($"Media de {stepsAverage:0} pasos al día.");
        if (streak.Current >= 2)
            highlights.Add($"Llevas una racha de {streak.Current} días.");

        var headline = activeDays switch
        {
            0 => "Esta semana no hemos visto actividad. Un paso pequeño hoy cuenta.",
            >= 5 => "¡Semana muy constante!",
            _ when workoutsCompleted > previousWorkouts => "Vas a mejor que la semana pasada.",
            _ => $"Estuviste activo {activeDays} día{Plural(activeDays)} esta semana."
        };

        return new WeeklySummary(
            from,
            today,
            activeDays,
            workoutsCompleted,
            workoutsPlanned,
            previousWorkouts,
            mealsLogged,
            coachQueries,
            feedback,
            averageSteps,
            headline,
            highlights);
    }

    private static IReadOnlyList<Achievement> BuildAchievements(
        UserActivitySnapshot snapshot,
        IReadOnlySet<DateOnly> progressDays,
        TimeSpan utcOffset)
    {
        var completedOn = snapshot.WorkoutDays
            .Where(day => day.CompletedAt is not null)
            .Select(day => AnalyticsCalendar.Day(day.CompletedAt!.Value, utcOffset))
            .Order()
            .ToArray();
        var coachDays = EventDays(snapshot, ProductEventNames.CoachQuery, utcOffset);
        var feedbackDays = EventDays(snapshot, ProductEventNames.WorkoutFeedbackSubmitted, utcOffset);
        var goalCreated = snapshot.Goals
            .Select(goal => AnalyticsCalendar.Day(goal.CreatedAt, utcOffset))
            .Order()
            .ToArray();
        var goalCompleted = snapshot.Goals
            .Where(goal => goal.Status == "Completed")
            .Select(goal => AnalyticsCalendar.Day(goal.UpdatedAt, utcOffset))
            .Order()
            .ToArray();
        var mealDays = snapshot.NutritionLogDates.Distinct().Order().ToArray();
        var streakMilestones = StreakMilestones(progressDays);
        var longest = CalculateStreak(progressDays.ToHashSet(), progressDays.Count == 0 ? DateOnly.MinValue : progressDays.Max()).Longest;

        return
        [
            Count("first_workout", "Primer entrenamiento", "Completa tu primer entrenamiento.", completedOn, 1),
            Count("workouts_10", "10 entrenamientos", "Completa 10 entrenamientos.", completedOn, 10),
            StreakAchievement("streak_3", "Racha de 3 días", 3, longest, streakMilestones),
            StreakAchievement("streak_7", "Una semana seguida", 7, longest, streakMilestones),
            StreakAchievement("streak_30", "30 días seguidos", 30, longest, streakMilestones),
            Count("first_coach_query", "Primera pregunta al Coach", "Pregunta algo al Coach sobre tus datos.", coachDays, 1),
            Count("feedback_5", "5 valoraciones", "Valora 5 sesiones para que el plan se adapte a ti.", feedbackDays, 5),
            Count("meals_7", "Una semana de comidas", "Registra comidas en 7 días distintos.", mealDays, 7),
            Count("first_goal", "Objetivo definido", "Crea tu primer objetivo.", goalCreated, 1),
            Count("goal_completed", "Objetivo cumplido", "Alcanza uno de tus objetivos.", goalCompleted, 1)
        ];
    }

    private static Achievement Count(string id, string title, string description, IReadOnlyList<DateOnly> orderedDays, int target)
    {
        var unlocked = orderedDays.Count >= target;
        return new Achievement(
            id,
            title,
            description,
            unlocked,
            unlocked ? orderedDays[target - 1] : null,
            Math.Min(orderedDays.Count, target),
            target);
    }

    private static Achievement StreakAchievement(
        string id,
        string title,
        int target,
        int longest,
        IReadOnlyDictionary<int, DateOnly> milestones)
    {
        var unlocked = longest >= target;
        return new Achievement(
            id,
            title,
            $"Registra progreso {target} días seguidos.",
            unlocked,
            unlocked && milestones.TryGetValue(target, out var reachedOn) ? reachedOn : null,
            Math.Min(longest, target),
            target);
    }

    /// <summary>First day each streak length (3, 7, 30) was reached.</summary>
    private static IReadOnlyDictionary<int, DateOnly> StreakMilestones(IReadOnlySet<DateOnly> progressDays)
    {
        var milestones = new Dictionary<int, DateOnly>();
        var run = 0;
        DateOnly? previous = null;
        foreach (var day in progressDays.Order())
        {
            run = previous is { } last && day.DayNumber == last.DayNumber + 1 ? run + 1 : 1;
            foreach (var target in new[] { 3, 7, 30 })
            {
                if (run == target)
                    milestones.TryAdd(target, day);
            }
            previous = day;
        }
        return milestones;
    }

    private static IReadOnlyList<Reminder> BuildReminders(
        UserActivitySnapshot snapshot,
        DateOnly today,
        TimeSpan utcOffset,
        Streak streak,
        WeeklySummary weekly,
        IReadOnlySet<DateOnly> progressDays)
    {
        var reminders = new List<Reminder>();
        var pendingWorkout = snapshot.WorkoutDays
            .Where(day => day.PlannedOn == today && day.CompletedAt is null)
            .OrderBy(day => day.Title, StringComparer.Ordinal)
            .FirstOrDefault();
        if (pendingWorkout is not null)
        {
            reminders.Add(new Reminder(
                ReminderKinds.PendingWorkout,
                "Entrenamiento pendiente",
                $"Hoy toca: {pendingWorkout.Title}.",
                "aifitnesscoach://workouts",
                today));
        }

        var hasActiveGoal = snapshot.Goals.Any(goal => AdherenceEngine.IsActiveOn(goal, today, utcOffset));
        if (hasActiveGoal && !progressDays.Contains(today))
        {
            var body = streak.Current > 0
                ? $"Registra un entrenamiento o una comida para mantener tu racha de {streak.Current} día{Plural(streak.Current)}."
                : "Registra un entrenamiento o una comida para avanzar hacia tu objetivo.";
            reminders.Add(new Reminder(
                ReminderKinds.DailyGoal,
                "Tu objetivo de hoy",
                body,
                pendingWorkout is null ? "aifitnesscoach://nutrition" : "aifitnesscoach://workouts",
                today));
        }

        // Next Sunday (today when it is Sunday): the app schedules it for the evening.
        var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)today.DayOfWeek + 7) % 7;
        var summaryBody = weekly.WorkoutsCompleted > 0 || weekly.ActiveDays > 0
            ? $"Esta semana: {weekly.ActiveDays} día{Plural(weekly.ActiveDays)} activo{Plural(weekly.ActiveDays)} y {weekly.WorkoutsCompleted} entrenamiento{Plural(weekly.WorkoutsCompleted)}. Mira tu resumen."
            : "Mira tu resumen semanal y planifica la próxima semana.";
        reminders.Add(new Reminder(
            ReminderKinds.WeeklySummary,
            "Tu resumen semanal",
            summaryBody,
            "aifitnesscoach://summary",
            today.AddDays(daysUntilSunday)));
        return reminders;
    }

    private static int CountEvents(UserActivitySnapshot snapshot, string name, Func<DateOnly, bool> inPeriod, TimeSpan utcOffset) =>
        snapshot.Events.Count(item => item.Name == name && inPeriod(AnalyticsCalendar.Day(item.OccurredAt, utcOffset)));

    private static DateOnly[] EventDays(UserActivitySnapshot snapshot, string name, TimeSpan utcOffset) =>
        snapshot.Events
            .Where(item => item.Name == name)
            .Select(item => AnalyticsCalendar.Day(item.OccurredAt, utcOffset))
            .Order()
            .ToArray();

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}
