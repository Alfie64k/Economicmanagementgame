using System;
using Godot;
using Sim.Core.Engine;
using Sim.Core.Model;
using Sim.Core.Policy;
using EconGame.App;

namespace EconGame.Ui;

/// <summary>Shared presentation of the staged turn plan: feedback text and the stage/withdraw toggle used by action buttons.</summary>
public static class PlanUi
{
    public static string Feedback(CommandResult r, Command cmd)
    {
        if (r.Unstaged) return "↶ " + r.Message;
        if (r.Staged)
            return $"✔ {(r.Replaced ? "Plan updated" : "Added to the plan")}: {PlanText.Describe(Game.World, cmd)}" + (r.PcCost > 0 ? $" — {r.PcCost:0} political capital when the turn is played" : "");
        if (r.NoOp) return "· " + r.Message;
        return "✘ " + r.Message;
    }

    /// <summary>Stage a command and report the outcome to <paramref name="report"/>.</summary>
    public static CommandResult Stage(Command cmd, Action<string>? report = null)
    {
        var r = Game.Stage(cmd); report?.Invoke(Feedback(r, cmd)); return r;
    }

    public static bool IsStaged(Command cmd) => Game.Staged(Simulation.KeyOf(cmd)) != null;

    /// <summary>A button that adds <paramref name="cmd"/> to the plan, or withdraws it when it is already there.</summary>
    public static Button Toggle(string label, Command cmd, Action<string>? report = null, bool accent = false, int minW = 0)
    {
        string key = Simulation.KeyOf(cmd);
        bool staged = Game.Staged(key) != null;
        var dry = CommandProcessor.Apply(Game.World, cmd, dryRun: true);
        var b = UI.Btn(staged ? $"✓ {label} — in plan" : $"{label}  ({dry.PcCost:0} PC)", () =>
        {
            if (staged) { Game.Unstage(key); report?.Invoke("↶ Withdrawn from the plan"); }
            else Stage(cmd, report);
        }, accent && !staged, minW);
        b.TooltipText = staged ? "Staged for the end of this turn. Click to withdraw it." : dry.Ok ? $"Adds to this turn's plan. Costs {dry.PcCost:0} political capital when the turn is played." : dry.Message;
        b.Disabled = !staged && !dry.Ok && dry.PcCost == 0 && !dry.NoOp;
        return b;
    }
}
