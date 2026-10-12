using KoaZa.Foreseer.Models;

namespace KoaZa.Foreseer.Services;

/// <summary>Poisson market probabilities based on the user's locally accumulated completed-match history.
/// The service intentionally refuses to fabricate probabilities when there is not enough local history.</summary>
public sealed class PredictionService
{
    public List<PredictionRow> Predict(Fixture fixture, IReadOnlyCollection<Fixture> history)
    {
        var homeMatches = history.Where(f => f.Status == "FT" && f.HomeGoals.HasValue && f.AwayGoals.HasValue && (f.HomeTeam == fixture.HomeTeam || f.AwayTeam == fixture.HomeTeam)).ToList();
        var awayMatches = history.Where(f => f.Status == "FT" && f.HomeGoals.HasValue && f.AwayGoals.HasValue && (f.HomeTeam == fixture.AwayTeam || f.AwayTeam == fixture.AwayTeam)).ToList();
        if (homeMatches.Count < 5 || awayMatches.Count < 5)
            return new List<PredictionRow> { new() { Market="تمام بازارها", Outcome="احتمال محاسبه نشد", Confidence="دادهٔ ناکافی", Note=$"برای مدل محلی، حداقل ۵ بازی کامل برای هر تیم لازم است (میزبان: {homeMatches.Count}، میهمان: {awayMatches.Count})." } };

        double AvgFor(List<Fixture> games, string team) => games.Average(f => f.HomeTeam == team ? f.HomeGoals!.Value : f.AwayGoals!.Value);
        double AvgAgainst(List<Fixture> games, string team) => games.Average(f => f.HomeTeam == team ? f.AwayGoals!.Value : f.HomeGoals!.Value);
        var lambdaHome = Math.Clamp((AvgFor(homeMatches, fixture.HomeTeam) + AvgAgainst(awayMatches, fixture.AwayTeam)) / 2.0, 0.15, 4.5);
        var lambdaAway = Math.Clamp((AvgFor(awayMatches, fixture.AwayTeam) + AvgAgainst(homeMatches, fixture.HomeTeam)) / 2.0, 0.15, 4.5);
        var home = Distribution(lambdaHome); var away = Distribution(lambdaAway);
        double pHome=0, pDraw=0, pAway=0, pOver25=0, pBtts=0;
        for (var h=0; h<9; h++) for (var a=0; a<9; a++)
        {
            var p=home[h]*away[a]; if (h>a) pHome+=p; else if (h==a) pDraw+=p; else pAway+=p;
            if (h+a>2) pOver25+=p; if (h>0 && a>0) pBtts+=p;
        }
        var rows = new List<PredictionRow>();
        void Add(string market, string outcome, double p) => rows.Add(new PredictionRow { Market=market, Outcome=outcome, Probability=$"{p:P1}", Confidence="ابتدایی", Note="مدل پواسون ساده؛ برآورد آماری است و تضمین نتیجه نیست." });
        Add("نتیجهٔ نهایی", "برد میزبان", pHome); Add("نتیجهٔ نهایی", "مساوی", pDraw); Add("نتیجهٔ نهایی", "برد میهمان", pAway);
        Add("مجموع گل‌ها", "بالای ۲٫۵ گل", pOver25); Add("گل‌زنی دو تیم", "هر دو تیم گل می‌زنند", pBtts);
        return rows;
    }

    private static double[] Distribution(double lambda)
    {
        var p = new double[9]; p[0]=Math.Exp(-lambda);
        for (var i=1;i<p.Length;i++) p[i]=p[i-1]*lambda/i;
        return p;
    }
}
