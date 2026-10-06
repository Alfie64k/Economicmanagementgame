using Sim.Core.Model;
using Sim.Core.Util;

namespace Sim.Core.Events;

/// <summary>Elections in democracies; coups and revolutions elsewhere. Player losses can end the game.</summary>
public static class Politics
{
    public static void Init(World w)
    {
        for (int i = 0; i < w.Countries.Count; i++)
        {
            var c = w.Countries[i];
            c.ElectionTerm = c.Id is "GBR" or "FRA" or "AUS" or "CAN" ? 60 : 48;
            c.NextElectionMonth = c.Democracy > 0.4 ? 6 + (int)(w.CountryRng[i].NextDouble() * (c.ElectionTerm - 6)) : -1;
        }
    }

    public static void Step(World w)
    {
        for (int i = 0; i < w.Countries.Count; i++)
        {
            var c = w.Countries[i]; var rng = w.CountryRng[i];
            bool player = c.Id == w.PlayerId;

            // coalition cohesion follows approval
            if (c.Gov == "democracy") c.Coalition += (0.3 + 0.6 * c.Approval - c.Coalition) * 0.02;
            c.Coalition = Maths.Clamp(c.Coalition, 0.1, 1);

            if (c.NextElectionMonth > 0 && w.Month >= c.NextElectionMonth)
            {
                double pWin = Maths.Sigmoid(10 * (c.Approval - 0.42));
                bool win = rng.Chance(pWin);
                c.NextElectionMonth = w.Month + c.ElectionTerm;
                if (win)
                {
                    c.PoliticalCapital = Math.Min(100, c.PoliticalCapital + 20); c.Coalition = Math.Min(1, c.Coalition + 0.1);
                    w.Log.Add(new LogEntry { Month = w.Month, Country = c.Id, Kind = "news", Text = $"{(player ? "You win" : c.Name + "'s government wins")} the election ({Fmt.P(pWin, 0)} chance at {Fmt.P(c.Approval, 0)} approval)." });
                }
                else if (player && w.GameOverOnLoss)
                {
                    w.GameOver = true; w.GameOverReason = $"Defeated at the polls with {Fmt.P(c.Approval, 0)} approval.";
                    w.Log.Add(new LogEntry { Month = w.Month, Country = c.Id, Kind = "news", Text = "You lose the election. The voters have had enough." });
                }
                else if (player)
                {
                    c.Approval = 0.45; c.PoliticalCapital = 25;
                    w.Log.Add(new LogEntry { Month = w.Month, Country = c.Id, Kind = "news", Text = "You lose the election but carry on in sandbox mode with a reshuffled cabinet and 25 political capital." });
                }
                else GovernmentChange(w, c);
            }

            // coups and revolutions in less democratic systems
            if (c.Democracy < 0.55)
            {
                double u = rng.NextDouble();
                double hazard = 0.002 * Maths.Clamp((0.30 - c.Stability) / 0.10, 0, 3) * (0.5 + c.Unrest);
                if (u < hazard)
                {
                    if (player) { w.GameOver = true; w.GameOverReason = "Overthrown in a coup."; w.Log.Add(new LogEntry { Month = w.Month, Country = c.Id, Kind = "crisis", Text = "You have been overthrown." }); }
                    else RegimeChange(w, c, "coup");
                }
            }
        }
    }

    static void GovernmentChange(World w, CountryState c)
    {
        c.Style = c.Style == "populist" ? "technocrat" : "populist";
        c.Approval = 0.50; c.Coalition = 0.5; c.PoliticalCapital = 50;
        w.Log.Add(new LogEntry { Month = w.Month, Country = c.Id, Kind = "news", Text = $"{c.Name}: government changes hands; the new administration is more {(c.Style == "populist" ? "populist" : "technocratic")}." });
    }

    public static void RegimeChange(World w, CountryState c, string cause)
    {
        c.Democracy = Maths.Clamp(c.Democracy + (c.Democracy < 0.3 ? 0.15 : -0.1), 0.02, 0.98);
        c.Style = "populist"; c.Stability = 0.5; c.Approval = 0.45; c.Unrest *= 0.5;
        w.Log.Add(new LogEntry { Month = w.Month, Country = c.Id, Kind = "news", Text = $"{c.Name}: regime change ({cause})." });
    }
}
