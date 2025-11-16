namespace amFTPd.Scripting;

/// <summary>
/// Represents a script engine for processing and evaluating rules defined in a custom AMScript file.
/// </summary>
/// <remarks>The <see cref="AMScriptEngine"/> is designed to load, parse, and evaluate rules from a
/// specified script file. Rules are defined in a custom syntax and can be used to evaluate conditions and apply
/// actions based on the provided context. The engine supports dynamic reloading of the script file when changes are
/// detected.</remarks>
public sealed class AMScriptEngine
{
    private readonly List<AMRule> _rules = new();
    private readonly string _filePath;
    private FileSystemWatcher? _watcher;

    public Action<string>? DebugLog { get; set; }

    public AMScriptEngine(string filePath)
    {
        _filePath = filePath;
        Load();
        Watch();
    }

    // ---------------------------------------------------------
    // Load + Watch
    // ---------------------------------------------------------

    public void Load()
    {
        _rules.Clear();

        if (!File.Exists(_filePath))
        {
            DebugLog?.Invoke($"[AMScript] Rule file not found: {_filePath}");
            return;
        }

        var lines = File.ReadAllLines(_filePath);

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('#')) continue;

            if (!line.StartsWith("if", StringComparison.OrdinalIgnoreCase))
                continue;

            int start = line.IndexOf('(');
            int end = line.IndexOf(')');
            if (start < 0 || end < start) continue;

            var cond = line.Substring(start + 1, end - start - 1).Trim();
            var action = line[(end + 1)..].Trim().TrimEnd(';');

            if (cond.Length == 0 || action.Length == 0)
                continue;

            _rules.Add(new AMRule(cond, action));
        }

        DebugLog?.Invoke($"[AMScript] Loaded {_rules.Count} rules from {_filePath}");
    }

    private void Watch()
    {
        var dir = Path.GetDirectoryName(_filePath);
        var file = Path.GetFileName(_filePath);
        if (dir is null || file is null) return;

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite
        };

        _watcher.Changed += (_, _) =>
        {
            // Debounce a bit to avoid partial writes
            Task.Delay(100).Wait();
            try { Load(); } catch { /* ignore */ }
        };

        _watcher.EnableRaisingEvents = true;
    }

    // ---------------------------------------------------------
    // Public evaluation entrypoints
    // ---------------------------------------------------------

    public AMScriptResult EvaluateDownload(AMScriptContext ctx)
        => EvaluateInternal(ctx);

    public AMScriptResult EvaluateUpload(AMScriptContext ctx)
        => EvaluateInternal(ctx);

    // Generic: we treat download/upload same in v1, context decides meaning
    private AMScriptResult EvaluateInternal(AMScriptContext ctx)
    {
        foreach (var rule in _rules)
        {
            if (EvaluateCondition(ctx, rule.Condition))
            {
                return ApplyAction(ctx, rule.Action);
            }
        }

        return AMScriptResult.NoChange(ctx);
    }

    // ---------------------------------------------------------
    // Condition evaluation
    // ---------------------------------------------------------

    private bool EvaluateCondition(AMScriptContext ctx, string cond)
    {
        cond = cond.Trim();
        if (cond.Length == 0) return false;

        // OR
        var orParts = cond.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var or in orParts)
        {
            if (EvaluateAndPart(ctx, or))
                return true;
        }
        return false;
    }

    private bool EvaluateAndPart(AMScriptContext ctx, string cond)
    {
        var andParts = cond.Split(new[] { "&&" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in andParts)
        {
            if (!EvaluateAtomic(ctx, part.Trim()))
                return false;
        }
        return true;
    }

    private bool EvaluateAtomic(AMScriptContext ctx, string atom)
    {
        atom = atom.Trim();
        if (atom.Length == 0) return false;

        // !expr
        if (atom[0] == '!')
            return !EvaluateAtomic(ctx, atom[1..].Trim());

        if (atom.Contains("==", StringComparison.Ordinal))
        {
            var p = atom.Split(new[] { "==" }, 2, StringSplitOptions.None);
            return GetValue(ctx, p[0].Trim()) == GetValue(ctx, p[1].Trim());
        }

        if (atom.Contains("!=", StringComparison.Ordinal))
        {
            var p = atom.Split(new[] { "!=" }, 2, StringSplitOptions.None);
            return GetValue(ctx, p[0].Trim()) != GetValue(ctx, p[1].Trim());
        }

        // Boolean check: treat token as variable
        var v = GetValue(ctx, atom);
        return v.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private string GetValue(AMScriptContext ctx, string token)
    {
        token = token.Trim();

        // String literal
        if (token.Length >= 2 && token[0] == '"' && token[^1] == '"')
            return token[1..^1];

        return token switch
        {
            "$is_fxp" => ctx.IsFxp.ToString(),
            "$section" => ctx.Section,
            "$freeleech" => ctx.FreeLeech.ToString(),
            "$user.name" => ctx.UserName,
            "$user.group" => ctx.UserGroup,
            "$bytes" => ctx.Bytes.ToString(),
            "$kb" => ctx.Kb.ToString(),
            "$cost_download" => ctx.CostDownload.ToString(),
            "$earned_upload" => ctx.EarnedUpload.ToString(),
            _ => token // unknown tokens: return as-is
        };
    }

    // ---------------------------------------------------------
    // Action evaluation
    // ---------------------------------------------------------

    private AMScriptResult ApplyAction(AMScriptContext ctx, string action)
    {
        action = action.Trim();

        if (action.Equals("return allow", StringComparison.OrdinalIgnoreCase))
            return AMScriptResult.Allow(ctx);

        if (action.Equals("return deny", StringComparison.OrdinalIgnoreCase))
            return AMScriptResult.Deny(ctx);

        long cost = ctx.CostDownload;
        long earn = ctx.EarnedUpload;
        string? msg = null;

        // Very simple grammar: single statement, v1
        if (action.StartsWith("cost_download", StringComparison.OrdinalIgnoreCase))
        {
            ParseNumericAssignment(action, ref cost);
        }
        if (action.StartsWith("earned_upload", StringComparison.OrdinalIgnoreCase))
        {
            ParseNumericAssignment(action, ref earn);
        }
        if (action.StartsWith("log", StringComparison.OrdinalIgnoreCase))
        {
            var body = action["log".Length..].Trim();
            if (body.Length >= 2 && body[0] == '"' && body[^1] == '"')
                msg = body[1..^1];
            DebugLog?.Invoke($"[AMScript] {msg}");
        }
        // return deny "message"
        if (action.StartsWith("return deny", StringComparison.OrdinalIgnoreCase))
        {
            msg = action["return deny".Length..].Trim().Trim('"');
            return AMScriptResult.DenyWithReason(msg);
        }

        // return output "TEXT"
        if (action.StartsWith("return output", StringComparison.OrdinalIgnoreCase))
        {
            msg = action["return output".Length..].Trim().Trim('"');
            return AMScriptResult.CustomOutput(msg);
        }

        // return override (for SITE)
        if (action.StartsWith("return override", StringComparison.OrdinalIgnoreCase))
        {
            return AMScriptResult.SiteOverride();
        }

        // return section "NAME"
        if (action.StartsWith("return section", StringComparison.OrdinalIgnoreCase))
        {
            var name = action["return section".Length..].Trim().Trim('"');
            return new AMScriptResult(
                AMRuleAction.Allow,
                ctx.CostDownload,
                ctx.EarnedUpload,
                Message: $"SECTION_OVERRIDE::{name}"
            );
        }

        // return set_dl 123
        if (action.StartsWith("return set_dl", StringComparison.OrdinalIgnoreCase))
        {
            var v = int.Parse(action["return set_dl".Length..].Trim());
            return new AMScriptResult(AMRuleAction.Allow, ctx.CostDownload, ctx.EarnedUpload,
                NewDownloadLimit: v);
        }

        // return set_ul 456
        if (action.StartsWith("return set_ul", StringComparison.OrdinalIgnoreCase))
        {
            var v = int.Parse(action["return set_ul".Length..].Trim());
            return new AMScriptResult(AMRuleAction.Allow, ctx.CostDownload, ctx.EarnedUpload,
                NewUploadLimit: v);
        }

        // return add_credits 100
        if (action.StartsWith("return add_credits", StringComparison.OrdinalIgnoreCase))
        {
            var v = long.Parse(action["return add_credits".Length..].Trim());
            return new AMScriptResult(AMRuleAction.Allow, ctx.CostDownload, ctx.EarnedUpload,
                CreditDelta: v);
        }

        // return sub_credits 50
        if (action.StartsWith("return sub_credits", StringComparison.OrdinalIgnoreCase))
        {
            var v = long.Parse(action["return sub_credits".Length..].Trim());
            return new AMScriptResult(AMRuleAction.Allow, ctx.CostDownload, ctx.EarnedUpload,
                CreditDelta: -v);
        }


        return new AMScriptResult(AMRuleAction.None, cost, earn, msg);
    }

    private static void ParseNumericAssignment(string expr, ref long value)
    {
        // Forms:
        // cost_download = 123
        // cost_download *= 2
        expr = expr.Trim();

        if (expr.Contains("*="))
        {
            var parts = expr.Split(new[] { "*=" }, 2, StringSplitOptions.None);
            if (long.TryParse(parts[1].Trim(), out var mul))
                value *= mul;
        }
        else if (expr.Contains("=", StringComparison.Ordinal))
        {
            var parts = expr.Split('=', 2);
            if (long.TryParse(parts[1].Trim(), out var set))
                value = set;
        }
    }
}