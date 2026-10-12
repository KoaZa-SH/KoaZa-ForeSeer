using KoaZa.Foreseer.Models;

namespace KoaZa.Foreseer.Services;

/// <summary>
/// Transparent baseline Poisson model built from locally stored completed fixtures.
/// Probabilities are estimates, not guarantees; the service refuses to estimate when history is insufficient.
/// </summary>
public sealed class PredictionService
{
    private const int MinimumMatchesPerTeam = 5;
    private const int MaxGoalsInGrid = 8;

    public List<PredictionRow> Predict(Fixture fixture, IReadOnlyCollection<Fixture> history)
    {
        var completed = history
            .Where(f => f.Status == "FT" && f.HomeGoals.HasValue && f.AwayGoals.HasValue
                        && f.HomeGoals.Value >= 0 && f.AwayGoals.Value >= 0)
            .ToList();

        var homeMatches = completed
            .Where(f => string.Equals(f.HomeTeam, fixture.HomeTeam, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(f.AwayTeam, fixture.HomeTeam, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.KickoffUtc)
            .Take(20)
            .ToList();

        var awayMatches = completed
            .Where(f => string.Equals(f.HomeTeam, fixture.AwayTeam, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(f.AwayTeam, fixture.AwayTeam, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.KickoffUtc)
            .Take(20)
            .ToList();

        if (homeMatches.Count < MinimumMatchesPerTeam || awayMatches.Count < MinimumMatchesPerTeam)
        {
            return new List<PredictionRow>
            {
                new()
                {
                    Market = "تمام بازارها",
                    Outcome = "احتمال محاسبه نشد",
                    Confidence = "دادهٔ ناکافی",
                    Note = $"برای مدل محلی حداقل {MinimumMatchesPerTeam} بازی کامل برای هر تیم لازم است؛ میزبان: {homeMatches.Count}، میهمان: {awayMatches.Count}."
                }
            };
        }

        static double GoalsFor(Fixture f, string team) =>
            string.Equals(f.HomeTeam, team, StringComparison.OrdinalIgnoreCase) ? f.HomeGoals!.Value : f.AwayGoals!.Value;

        static double GoalsAgainst(Fixture f, string team) =>
            string.Equals(f.HomeTeam, team, StringComparison.OrdinalIgnoreCase) ? f.AwayGoals!.Value : f.HomeGoals!.Value;

        var lambdaHome = Math.Clamp(
            (homeMatches.Average(f => GoalsFor(f, fixture.HomeTeam))
             + awayMatches.Average(f => GoalsAgainst(f, fixture.AwayTeam))) / 2.0, 0.15, 4.5);

        var lambdaAway = Math.Clamp(
            (awayMatches.Average(f => GoalsFor(f, fixture.AwayTeam))
             + homeMatches.Average(f => GoalsAgainst(f, fixture.HomeTeam))) / 2.0, 0.15, 4.5);

        var home = Distribution(lambdaHome);
        var away = Distribution(lambdaAway);
        var scorelines = new List<(int Home, int Away, double Probability)>();
        double pHome = 0, pDraw = 0, pAway = 0;
        double pOver15 = 0, pOver25 = 0, pOver35 = 0;
        double pBtts = 0, pHomeScores = 0, pAwayScores = 0;

        for (var h = 0; h <= MaxGoalsInGrid; h++)
        {
            for (var a = 0; a <= MaxGoalsInGrid; a++)
            {
                var p = home[h] * away[a];
                scorelines.Add((h, a, p));

                if (h > a) pHome += p;
                else if (h == a) pDraw += p;
                else pAway += p;

                if (h + a > 1) pOver15 += p;
                if (h + a > 2) pOver25 += p;
                if (h + a > 3) pOver35 += p;
                if (h > 0 && a > 0) pBtts += p;
                if (h > 0) pHomeScores += p;
                if (a > 0) pAwayScores += p;
            }
        }

        var rows = new List<PredictionRow>();
        var note = $"مدل پواسون پایه؛ میانگین گل مورد انتظار: میزبان {lambdaHome:F2}، میهمان {lambdaAway:F2}. برآورد آماری است، نه تضمین نتیجه.";
        var confidence = homeMatches.Count >= 12 && awayMatches.Count >= 12 ? "پایه؛ تاریخچهٔ بیشتر" : "پایه";

        void Add(string market, string outcome, double probability) =>
            rows.Add(new PredictionRow
            {
                Market = market,
                Outcome = outcome,
                Probability = $"{Math.Clamp(probability, 0, 1):P1}",
                Confidence = confidence,
                Note = note
            });

        Add("نتیجهٔ نهایی 1X2", "برد میزبان", pHome);
        Add("نتیجهٔ نهایی 1X2", "مساوی", pDraw);
        Add("نتیجهٔ نهایی 1X2", "برد میهمان", pAway);
        Add("دو شانس", "1X · میزبان یا مساوی", pHome + pDraw);
        Add("دو شانس", "X2 · مساوی یا میهمان", pDraw + pAway);
        Add("دو شانس", "12 · بدون مساوی", pHome + pAway);

        Add("مجموع گل‌ها", "بالای ۱٫۵ گل", pOver15);
        Add("مجموع گل‌ها", "زیر ۱٫۵ گل", 1 - pOver15);
        Add("مجموع گل‌ها", "بالای ۲٫۵ گل", pOver25);
        Add("مجموع گل‌ها", "زیر ۲٫۵ گل", 1 - pOver25);
        Add("مجموع گل‌ها", "بالای ۳٫۵ گل", pOver35);
        Add("مجموع گل‌ها", "زیر ۳٫۵ گل", 1 - pOver35);
        Add("گل‌زنی دو تیم", "هر دو تیم گل می‌زنند", pBtts);
        Add("گل‌زنی دو تیم", "حداقل یک تیم گل نمی‌زند", 1 - pBtts);
        Add("گل تیم", "میزبان حداقل یک گل می‌زند", pHomeScores);
        Add("گل تیم", "میهمان حداقل یک گل می‌زند", pAwayScores);

        foreach (var score in scorelines.OrderByDescending(s => s.Probability).Take(5))
            Add("نتیجهٔ دقیق (برترین برآوردها)", $"{score.Home} - {score.Away}", score.Probability);

        return rows;
    }

    private static double[] Distribution(double lambda)
    {
        var probabilities = new double[MaxGoalsInGrid + 1];
        probabilities[0] = Math.Exp(-lambda);
        for (var i = 1; i < probabilities.Length; i++)
            probabilities[i] = probabilities[i - 1] * lambda / i;

        // Normalize the finite grid so displayed market probabilities sum consistently.
        var total = probabilities.Sum();
        if (total > 0)
            for (var i = 0; i < probabilities.Length; i++)
                probabilities[i] /= total;

        return probabilities;
    }
}
