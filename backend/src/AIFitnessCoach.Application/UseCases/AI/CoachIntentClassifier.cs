using System.Text.RegularExpressions;

namespace AIFitnessCoach.Application.UseCases.AI;

public sealed partial class CoachIntentClassifier
{
    public CoachIntent Classify(string message, bool hasConversation)
    {
        var text = message.Trim();
        if (InjectionPattern().IsMatch(text))
            return CoachIntent.PromptInjectionAttempt;
        if (MedicalPattern().IsMatch(text))
            return CoachIntent.MedicalOrClinicalQuestion;
        if (UnsupportedPattern().IsMatch(text))
            return CoachIntent.UnsupportedRequest;
        if (ContainsAny(text, "pasos", "step", "caminar", "caminata"))
            return CoachIntent.StepTrendAnalysis;
        if (ContainsAny(text, "sueño", "dormí", "dormir", "descanso nocturno"))
            return CoachIntent.SleepTrendAnalysis;
        if (ContainsAny(text, "peso", "kg", "adelgazar", "bajar de peso"))
            return CoachIntent.WeightTrendAnalysis;
        if (ContainsAny(text, "constante", "constancia", "entrenamientos", "entrené", "entrenar"))
            return CoachIntent.WorkoutConsistencyAnalysis;
        if (ContainsAny(text, "dispones", "datos", "registrado", "registrada"))
            return CoachIntent.DataAvailabilityQuestion;
        if (ContainsAny(text, "mes", "mensual", "últimos 30", "ultimos 30"))
            return CoachIntent.MonthlyProgressAnalysis;
        if (ContainsAny(text, "semana", "semanal", "últimos 7", "ultimos 7"))
            return CoachIntent.WeeklyActivityAnalysis;
        if (hasConversation && ContainsAny(text, "eso", "¿y", "y si", "amplía", "amplia", "más"))
            return CoachIntent.ConversationFollowUp;
        if (ContainsAny(text, "qué hago", "que hago", "qué debería", "que deberia", "consejo", "mejorar",
                "actividad", "progreso", "bienestar", "salud general", "ejercicio", "hábitos", "habitos"))
            return CoachIntent.GeneralWellnessQuestion;
        return CoachIntent.UnsupportedRequest;
    }

    public static CoachIntentPolicy PolicyFor(CoachIntent intent) => intent switch
    {
        CoachIntent.WeeklyActivityAnalysis =>
            new(["weekly", "consistency", "workouts"], "last7Days", false, null),
        CoachIntent.MonthlyProgressAnalysis =>
            new(["monthly", "trends"], "last30Days", false, null),
        CoachIntent.StepTrendAnalysis =>
            new(["weeklySteps", "stepTrend"], "last7Days", false, null),
        CoachIntent.SleepTrendAnalysis =>
            new(["weeklySleep", "sleepTrend"], "last7Days", false, null),
        CoachIntent.WeightTrendAnalysis =>
            new(["weightTrend", "weightDataAvailability"], "last30Days", false, null),
        CoachIntent.WorkoutConsistencyAnalysis =>
            new(["consistency", "workouts"], "last7Days", false, null),
        CoachIntent.DataAvailabilityQuestion =>
            new(["dataAvailability"], "last7Days", false, null),
        CoachIntent.ConversationFollowUp =>
            new(["followUpContext"], "auto", false, null),
        CoachIntent.MedicalOrClinicalQuestion =>
            new([], "auto", true, "No puedo evaluar síntomas ni ofrecer consejo médico. Si te preocupa tu salud, consulta a un profesional sanitario. Puedo ayudarte con tendencias generales de actividad."),
        CoachIntent.PromptInjectionAttempt =>
            new([], "auto", true, "Puedo ayudar con actividad física general y datos agregados, pero no revelar instrucciones internas ni procesar esa solicitud."),
        CoachIntent.UnsupportedRequest =>
            new([], "auto", true, "Puedo responder preguntas generales sobre actividad y tendencias disponibles. No ejecuto acciones ni genero planes en esta fase."),
        _ => new(["weekly", "goal"], "last7Days", false, null)
    };

    private static bool ContainsAny(string value, params string[] words) =>
        words.Any(word => value.Contains(word, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(
        @"ignore (all |any )?(previous|prior|above) instructions|reveal (the )?(system )?prompt|show (me )?(the )?(api key|secret)|omite (todas )?(las )?instrucciones|revela(r)? (el )?(prompt|secreto|token)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InjectionPattern();

    [GeneratedRegex(
        @"diagn[oó]stic|s[ií]ntoma|medicaci[oó]n|tratamiento|lesi[oó]n|dolor|duele|molestia|rodilla|arritmia|trastorno del sue[nñ]o|enfermedad|frecuencia card[ií]aca|pulso|ritmo card[ií]aco|heart rate|clinical|medication|injury|pain",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MedicalPattern();

    [GeneratedRegex(
        @"(crea|genera|modifica|actualiza|borra|elimina|recomienda|recomendar).{0,30}(rutina|objetivo|healthkit|entrenamiento)|plan de entrenamiento",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnsupportedPattern();
}
