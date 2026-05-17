/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AMScriptEngine.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-16 06:46:16
 *  Last Modified:  2025-12-14 18:14:18
 *  CRC32:          0xC91D312B
 *  
 *  Description:
 *      Represents a script engine for processing and evaluating rules defined in a custom AMScript file.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */


using System.Diagnostics;

namespace amFTPd.Scripting;

/// <summary>
/// Represents a script engine for processing and evaluating rules defined in a custom AMScript file.
/// </summary>
/// <remarks>The <see cref="AMScriptEngine"/> is designed to load, parse, and evaluate rules from a
/// specified script file. Rules are defined in a custom syntax and can be used to evaluate conditions and apply
/// actions based on the provided context. The engine supports dynamic reloading of the script file when changes are
/// detected.</remarks>
public sealed class AMScriptEngine : IDisposable
{
    private List<AMRule> _rules = [];
    private readonly string? _filePath;
    private FileSystemWatcher? _watcher;

    public int MaxRulesPerEvaluation { get; set; } = 1024;
    public TimeSpan MaxEvaluationTime { get; set; } = TimeSpan.FromMilliseconds(100);

    private readonly object _gate = new();

    /// <summary>
    /// Gets or sets the delegate used to log debug messages.
    /// </summary>
    /// <remarks>Use this property to provide a custom logging mechanism for debug messages.  Assign a
    /// delegate that processes or outputs the debug messages as needed.</remarks>
    public Action<string>? DebugLog { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AMScriptEngine"/> class with no script file.
    /// </summary>
    public AMScriptEngine() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="AMScriptEngine"/> class with the specified script file.
    /// </summary>
    /// <param name="filePath">The path to the script file. This parameter cannot be null or empty.</param>
    public AMScriptEngine(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));

        _filePath = filePath;
        Load();

        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();

            _watcher = new FileSystemWatcher(dir, Path.GetFileName(filePath));
            _watcher.Changed += (_, _) =>
            {
                // Debounce a bit to avoid partial writes
                Thread.Sleep(100);
                try { Load(); } catch { /* ignore */ }
            };

            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            DebugLog?.Invoke($"[AMScript] Failed to start watcher for '{filePath}': {ex.Message}");
        }
    }

    private void Load()
    {
        if (_filePath == null || !File.Exists(_filePath)) return;

        try
        {
            var lines = File.ReadAllLines(_filePath);
            var newRules = new List<AMRule>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

                // Basic parsing: if (cond) action;
                if (trimmed.StartsWith("if", StringComparison.OrdinalIgnoreCase))
                {
                    var openParen = trimmed.IndexOf('(');
                    var closeParen = trimmed.LastIndexOf(')');
                    if (openParen != -1 && closeParen > openParen)
                    {
                        var cond = trimmed[(openParen + 1)..closeParen];
                        var action = trimmed[(closeParen + 1)..].Trim().TrimEnd(';');
                        newRules.Add(new AMRule(cond, action));
                    }
                }
                else
                {
                    // Action only rule (always runs)
                    newRules.Add(new AMRule("", trimmed.TrimEnd(';')));
                }
            }

            lock (_gate)
            {
                _rules = newRules;
            }
            DebugLog?.Invoke($"[AMScript] Loaded {newRules.Count} rules from '{_filePath}'");
        }
        catch (Exception ex)
        {
            DebugLog?.Invoke($"[AMScript] Failed to load '{_filePath}': {ex.Message}");
        }
    }

    /// <summary>
    /// Evaluates the upload rules defined in the current script engine.
    /// </summary>
    /// <param name="ctx">The <see cref="AMScriptContext"/> containing the upload details and parameters. This parameter cannot be null.</param>
    /// <returns>An <see cref="AMScriptResult"/> representing the outcome of the evaluation.</returns>
    public AMScriptResult EvaluateUpload(AMScriptContext ctx)
        => EvaluateInternal(ctx);

    /// <summary>
    /// Evaluates the download rules defined in the current script engine.
    /// </summary>
    /// <param name="ctx">The <see cref="AMScriptContext"/> containing the download details and parameters. This parameter cannot be null.</param>
    /// <returns>An <see cref="AMScriptResult"/> representing the result of the evaluation.</returns>
    public AMScriptResult EvaluateDownload(AMScriptContext ctx)
        => EvaluateInternal(ctx);

    /// <summary>
    /// Evaluates the user rules defined in the current script engine.
    /// </summary>
    /// <param name="ctx">The <see cref="AMScriptContext"/> containing the user details and parameters. This parameter cannot be null.</param>
    /// <returns>An <see cref="AMScriptResult"/> representing the outcome of the evaluation.</returns>
    public AMScriptResult EvaluateUser(AMScriptContext ctx)
        => EvaluateInternal(ctx);

    /// <summary>
    /// Evaluates the group rules defined in the current script engine.
    /// </summary>
    /// <param name="ctx">The <see cref="AMScriptContext"/> containing the group details and parameters. This parameter cannot be null.</param>
    /// <returns>An AMScriptResult representing the outcome of the group evaluation.</returns>
    public AMScriptResult EvaluateGroup(AMScriptContext ctx)
        => EvaluateInternal(ctx);

    private AMScriptResult EvaluateInternal(AMScriptContext ctx)
    {
        try
        {
            Stopwatch? sw = (MaxEvaluationTime > TimeSpan.Zero) ? Stopwatch.StartNew() : null;

            AMRule[] rules;
            lock (_gate)
            {
                rules = _rules.ToArray();
            }

            var checkedRules = 0;
            var currentCostDownload = ctx.CostDownload;
            var currentEarnedUpload = ctx.EarnedUpload;
            foreach (var rule in rules)
            {
                if (sw != null && sw.Elapsed > MaxEvaluationTime)
                {
                    DebugLog?.Invoke($"[AMScript] Timeout ({sw.Elapsed.TotalMilliseconds:0}ms) in '{_filePath}'");
                    return AMScriptResult.Error(ctx, "TIMEOUT", "Script evaluation timed out");
                }

                if (++checkedRules > MaxRulesPerEvaluation)
                    break;

                if (string.IsNullOrWhiteSpace(rule.Condition) || EvaluateCondition(ctx, rule.Condition))
                {
                    var effectiveCtx = ctx with
                    {
                        CostDownload = currentCostDownload,
                        EarnedUpload = currentEarnedUpload
                    };
                    var res = ApplyAction(effectiveCtx, rule.Action);
                    if (res.Action != AMRuleAction.None)
                        return res;

                    currentCostDownload = res.CostDownload;
                    currentEarnedUpload = res.EarnedUpload;
                }
            }

            return new AMScriptResult(
                AMRuleAction.None,
                currentCostDownload,
                currentEarnedUpload);
        }
        catch (Exception ex)
        {
            DebugLog?.Invoke($"[AMScript] Exception in '{_filePath}': {ex.Message}");
            return AMScriptResult.Error(ctx, "EXCEPTION", ex.Message);
        }
    }

    private bool EvaluateCondition(AMScriptContext ctx, string cond)
    {
        cond = cond.Trim();
        if (cond.Length == 0) return false;

        // OR
        var orParts = cond.Split(["||"], StringSplitOptions.RemoveEmptyEntries);
        return orParts.Any(or => EvaluateAndPart(ctx, or));
    }

    private bool EvaluateAndPart(AMScriptContext ctx, string cond)
    {
        var andParts = cond.Split(["&&"], StringSplitOptions.RemoveEmptyEntries);
        return andParts.All(part => EvaluateAtomic(ctx, part.Trim()));
    }

    private bool EvaluateAtomic(AMScriptContext ctx, string atom)
    {
        atom = atom.Trim();
        if (atom.Length == 0)
            return false;

        // Handle unary NOT
        if (atom.StartsWith('!'))
        {
            return !EvaluateAtomic(ctx, atom[1..].Trim());
        }

        // Comparison operators: ==, !=, >=, <=, >, <
        string[] ops = ["==", "!=", ">=", "<=", ">", "<"];
        foreach (var op in ops)
        {
            var idx = atom.IndexOf(op);
            if (idx != -1)
            {
                var left = atom[..idx].Trim();
                var right = atom[(idx + op.Length)..].Trim();
                return EvaluateComparison(ctx, left, op, right);
            }
        }

        // Boolean literal or variable
        var val = GetValue(ctx, atom);
        return val.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private bool EvaluateComparison(AMScriptContext ctx, string leftToken, string op, string rightToken)
    {
        var left = GetValue(ctx, leftToken);
        var right = GetValue(ctx, rightToken);

        // Numeric comparison if both are digits
        if (long.TryParse(left, out var lNum) && long.TryParse(right, out var rNum))
        {
            return op switch
            {
                "==" => lNum == rNum,
                "!=" => lNum != rNum,
                ">" => lNum > rNum,
                "<" => lNum < rNum,
                ">=" => lNum >= rNum,
                "<=" => lNum <= rNum,
                _ => false
            };
        }

        // String comparison
        return op switch
        {
            "==" => left.Equals(right, StringComparison.OrdinalIgnoreCase),
            "!=" => !left.Equals(right, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private string GetValue(AMScriptContext ctx, string token)
    {
        token = token.Trim();

        // String literal
        if (token.Length >= 2 && token[0] == '"' && token[^1] == '"')
            return token[1..^1];

        return token switch
        {
            "$is_fxp" => ctx.IsFxp ? "true" : "false",
            "$is_admin" => ctx.IsAdmin ? "true" : "false",
            "$is_siteop" => ctx.IsSiteop ? "true" : "false",
            "$is_tls" => ctx.IsTls ? "true" : "false",
            "$section" => ctx.Section,
            "$freeleech" => ctx.FreeLeech ? "true" : "false",
            "$user.name" => ctx.UserName,
            "$user.group" => ctx.UserGroup,
            "$bytes" => ctx.Bytes.ToString(),
            "$kb" => ctx.Kb.ToString(),
            "$cost_download" => ctx.CostDownload.ToString(),
            "$earned_upload" => ctx.EarnedUpload.ToString(),
            "$virtual_path" => ctx.VirtualPath ?? string.Empty,
            "$physical_path" => ctx.PhysicalPath ?? string.Empty,
            "$event" => ctx.Event,
            "$command" => ctx.Event,
            "$arg" => ctx.Arg,
            "$site.command" => ctx.Event.StartsWith("SITE ", StringComparison.OrdinalIgnoreCase) ? ctx.Event[5..] : ctx.Event,
            "$site.arg" => ctx.Arg,
            _ => token // unknown tokens: return as-is
        };
    }

    // ---------------------------------------------------------
    // Action evaluation
    // ---------------------------------------------------------

    private AMScriptResult ApplyAction(AMScriptContext ctx, string action)
    {
        action = action.Trim();

        // return allow;
        if (action.StartsWith("return allow", StringComparison.OrdinalIgnoreCase))
        {
            return new AMScriptResult(
                AMRuleAction.Allow,
                ctx.CostDownload,
                ctx.EarnedUpload
            );
        }

        // return deny ["reason"];
        if (action.StartsWith("return deny", StringComparison.OrdinalIgnoreCase))
        {
            var reason = (string?)null;
            var firstQuote = action.IndexOf('"');
            var lastQuote = action.LastIndexOf('"');
            if (firstQuote != -1 && lastQuote > firstQuote)
            {
                reason = action[(firstQuote + 1)..lastQuote];
            }

            return new AMScriptResult(
                AMRuleAction.Deny,
                ctx.CostDownload,
                ctx.EarnedUpload,
                DenyReason: reason
            );
        }

        // earned_upload *= 2;
        if (action.Contains("*="))
        {
            var parts = action.Split("*=");
            var varName = parts[0].Trim();
            if (varName.Equals("earned_upload", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(parts[1].TrimEnd(';').Trim(), out var mult))
                {
                    return new AMScriptResult(
                        AMRuleAction.None,
                        ctx.CostDownload,
                        (long)(ctx.EarnedUpload * mult)
                    );
                }
            }
        }

        // add_credits 1024;
        if (action.StartsWith("add_credits", StringComparison.OrdinalIgnoreCase))
        {
            var tail = action[11..].Trim().TrimEnd(';');
            if (long.TryParse(tail, out var add))
            {
                return new AMScriptResult(
                    AMRuleAction.Allow,
                    ctx.CostDownload,
                    ctx.EarnedUpload,
                    CreditDelta: add
                );
            }
        }

        return AMScriptResult.NoChange(ctx);
    }

    /// <summary>
    /// Releases all resources used by the <see cref="AMScriptEngine"/>.
    /// </summary>
    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
