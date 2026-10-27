using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Elynt.Core.Tweaks;
using Microsoft.Extensions.Logging;

namespace Elynt.Core.Repair;

/// <summary>Lists and runs repair tools for one user: streams progress, writes a plain summary and saves a log of every run.</summary>
internal sealed partial class RepairBench(
    RepairCatalogue catalogue,
    SystemAdapters system,
    IRepairLogStore logs,
    string user,
    TimeProvider clock,
    ILogger log)
{
    private IRepairSystem Pc => system.Repair ?? throw new InvalidOperationException("Repair tools aren't available here.");

    public IReadOnlyList<RepairToolView> List() => system.Repair is null ? [] : catalogue.Tools.Select(Describe).ToList();

    public async Task<RepairResult> RunAsync(string toolId, bool confirmed, IProgress<RepairProgress>? progress, CancellationToken cancellation)
    {
        if (system.Repair is null)
            return Refused(toolId, "Repair tools aren't available here. Nothing was changed.");
        if (!catalogue.TryGet(toolId, out var tool))
            return Refused(toolId, $"There is no repair tool called '{toolId}'.");
        if (!confirmed && tool.Confirmation is { } warning)
            return Refused(toolId, $"This tool needs your confirmation first. {warning} Nothing was changed.");
        var view = Describe(tool);
        if (!view.Available)
            return Refused(toolId, $"{view.NotAvailableReason} Nothing was changed.");

        var started = clock.GetUtcNow();
        var text = new StringBuilder();
        text.AppendLine("Elynt's Windows Optimizer repair log")
            .AppendLine($"Tool: {tool.Name} ({tool.Id})")
            .AppendLine($"Started: {started:O}")
            .AppendLine($"Windows build: {SafeOsBuild()}")
            .AppendLine($"User: {user}");

        var outcomes = new List<(RepairStep Step, StepOutcome? Outcome)>();
        var failed = false;
        var stopped = false;
        for (var i = 0; i < tool.Steps.Count; i++)
        {
            var step = tool.Steps[i];
            text.AppendLine().AppendLine($"== Step {i + 1}/{tool.Steps.Count}: {step.Name}").AppendLine($"> {step.Describe(Pc.SystemDrive)}");
            if ((failed || stopped) && !step.AlwaysRun)
            {
                text.AppendLine("Skipped: an earlier step didn't work.");
                outcomes.Add((step, null));
                continue;
            }

            progress?.Report(new(tool.Id, i + 1, tool.Steps.Count, step.Name, null, null));
            // A step that must always run (restarting a service) still runs after the user stopped the tool.
            var token = stopped ? CancellationToken.None : cancellation;
            StepOutcome outcome;
            try
            {
                outcome = step switch
                {
                    RunProgramStep run => await RunAsync(tool, i, run, run.Interpret, text, progress,
                        output => Pc.RunAsync(run.Resolve(Pc.SystemDrive), output, token)),
                    ResetAppStep reset => await RunAsync(tool, i, reset, reset.Interpret, text, progress,
                        output => Pc.ResetAppAsync(reset.PackageName, user, output, token)),
                    ClearFolderStep clear => ClearFolder(clear, text),
                    _ => throw new NotSupportedException(step.GetType().Name),
                };
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                stopped = true;
                outcome = StepOutcome.Failed("Stopped before it finished.");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Repair step {Step} of {Tool} failed", step.Name, tool.Id);
                text.AppendLine($"Error: {ex.Message}");
                outcome = StepOutcome.Failed($"Couldn't run: {ex.Message}");
            }

            text.AppendLine($"Result: {outcome.Summary}");
            failed |= outcome.Status == StepStatus.Failed;
            outcomes.Add((step, outcome));
        }

        var summary = Summarize(outcomes, stopped);
        var restart = outcomes.Any(o => o.Outcome?.RestartNeeded == true);
        var followUp = outcomes.Select(o => o.Outcome?.FollowUpToolId).FirstOrDefault(id => id is not null);
        text.AppendLine().AppendLine("== Summary").AppendLine(summary).AppendLine($"Finished: {clock.GetUtcNow():O}");

        var logText = text.ToString();
        string? location = null;
        try
        {
            location = logs.Save($"repair-{started:yyyyMMdd-HHmmss}-{tool.Id.Replace("repair.", "")}.log", logText);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Couldn't save the log of {Tool}", tool.Id);
            summary += $"{Environment.NewLine}The log couldn't be saved ({ex.Message}); use Copy log instead.";
        }

        log.LogInformation("Repair {Tool} finished: {Summary}", tool.Id, summary);
        return new(tool.Id, !failed, summary, restart, followUp, location, logText);
    }

    /// <summary>Runs a step that prints output and ends with an exit code, streaming each line to the log and the page.</summary>
    private static async Task<StepOutcome> RunAsync(
        RepairToolDefinition tool, int index, RepairStep step, Func<int, string, StepOutcome> interpret, StringBuilder text,
        IProgress<RepairProgress>? progress, Func<Action<string>, Task<int>> run)
    {
        var output = new StringBuilder();
        var exitCode = await run(line =>
        {
            output.AppendLine(line);
            text.AppendLine(line);
            progress?.Report(new(tool.Id, index + 1, tool.Steps.Count, step.Name, Percent(line), line));
        });
        text.AppendLine($"Exit code: {exitCode}");
        return interpret(exitCode, output.ToString());
    }

    private StepOutcome ClearFolder(ClearFolderStep step, StringBuilder text)
    {
        var cleared = Pc.ClearFolder(step.Root, step.RelativePath, step.Pattern, step.IncludeSubfolders, user);
        text.AppendLine($"Removed {cleared.Removed} file(s); {cleared.InUse} in use.");
        return cleared.InUse == 0
            ? StepOutcome.Ok($"Removed {cleared.Removed} file(s).")
            : StepOutcome.Ok($"Removed {cleared.Removed} file(s); {cleared.InUse} in use are rebuilt after a restart.", restartNeeded: true);
    }

    private static string Summarize(List<(RepairStep Step, StepOutcome? Outcome)> outcomes, bool stopped)
    {
        var headline =
            stopped ? "Stopped. Steps that put things back still ran; see below." :
            outcomes.Any(o => o.Outcome?.Status == StepStatus.Failed) ? "Some steps didn't work; see below. The log has the details." :
            outcomes.Any(o => o.Outcome?.Status == StepStatus.ProblemsFound) ? "Done, and it found problems; see below." :
            "Done.";
        if (outcomes.Any(o => o.Outcome?.RestartNeeded == true))
            headline += " Restart the PC to finish.";

        var lines = outcomes.Select(o => $"• {o.Step.Name}: {o.Outcome?.Summary ?? "Skipped because an earlier step didn't work."}");
        return string.Join(Environment.NewLine, [headline, .. lines]);
    }

    private static RepairResult Refused(string toolId, string message) => new(toolId, false, message, false, null, null, "");

    private string SafeOsBuild()
    {
        try
        {
            return system.System.OsBuild;
        }
        catch (Exception ex)
        {
            return $"unknown ({ex.Message})";
        }
    }

    private static double? Percent(string line) =>
        PercentPattern().Match(line) is { Success: true } m && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value <= 100
            ? value
            : null;

    [GeneratedRegex(@"(\d{1,3}(?:\.\d+)?)\s*%")]
    private static partial Regex PercentPattern();

    private RepairToolView Describe(RepairToolDefinition tool)
    {
        var missing = tool.Requires is { } need && !Pc.FolderExists(need.Root, need.RelativePath, user) ? need.ReasonWhenMissing : null;
        return new(
            tool.Id,
            Sections.Repair,
            tool.Name,
            tool.WhatItDoes,
            tool.Steps.Select(step => step.Describe(Pc.SystemDrive)).ToList(),
            tool.Restart,
            tool.RestartText,
            tool.Confirmation,
            tool.IsFollowUp,
            missing is null,
            missing);
    }
}
