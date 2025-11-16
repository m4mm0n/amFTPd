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

    public Action<string>? DebugLog;

    public AMScriptEngine(string filePath)
    {
        _filePath = filePath;
        Load();
        Watch();
    }

    public void Load()
    {
        _rules.Clear();

        if (!File.Exists(_filePath))
        {
            DebugLog?.Invoke($"[AMScript] No rule file found at {_filePath}");
            return;
        }

        var lines = File.ReadAllLines(_filePath);

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('#')) continue;

            // expected: if (<condition>) <action>;
            if (!line.StartsWith("if")) continue;

            int start = line.IndexOf('(');
            int end = line.IndexOf(')');
            if (start < 0 || end < start) continue;

            string condition = line.Substring(start + 1, end - start - 1).Trim();
            string action = line[(end + 1)..].Trim().TrimEnd(';');

            _rules.Add(new AMRule(condition, action));
        }

        DebugLog?.Invoke($"[AMScript] Loaded {_rules.Count} rules.");
    }

    private AMScriptResult ApplyAction(AMScriptContext ctx, string action, bool isDownload)
    {
        action = action.Trim().ToLower();

        if (action == "return allow")
            return AMScriptResult.Allow(ctx);

        if (action == "return deny")
            return AMScriptResult.Deny(ctx);

        long cost = ctx.CostDownload;
        long earn = ctx.EarnedUpload;

        if (action.StartsWith("cost_download ="))
        {
            long v = long.Parse(action.Split('=')[1].Trim());
            cost = v;
        }
        else if (action.StartsWith("cost_download *="))
        {
            long v = long.Parse(action.Split("*=")[1].Trim());
            cost *= v;
        }
        else if (action.StartsWith("earned_upload ="))
        {
            long v = long.Parse(action.Split('=')[1].Trim());
            earn = v;
        }
        else if (action.StartsWith("earned_upload *="))
        {
            long v = long.Parse(action.Split("*=")[1].Trim());
            earn *= v;
        }
        else if (action.StartsWith("log"))
        {
            var msg = action.Substring(3).Trim().Trim('"');
            DebugLog?.Invoke($"[AMScript] {msg}");
        }

        return new AMScriptResult(AMRuleAction.None, cost, earn);
    }

    private void Watch()
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        var file = Path.GetFileName(_filePath)!;

        _watcher = new FileSystemWatcher(dir, file);
        _watcher.NotifyFilter = NotifyFilters.LastWrite;

        _watcher.Changed += (_, __) =>
        {
            Task.Delay(100).Wait(); // debounce
            try
            {
                Load();
            }
            catch
            {
            }
        };

        _watcher.EnableRaisingEvents = true;
    }

    // Evaluate for download
    public AMScriptResult EvaluateDownload(AMScriptContext ctx)
        => Evaluate(ctx, true);

    // Evaluate for upload
    public AMScriptResult EvaluateUpload(AMScriptContext ctx)
        => Evaluate(ctx, false);

    private AMScriptResult Evaluate(AMScriptContext ctx, bool isDownload)
    {
        foreach (var r in _rules)
        {
            if (EvaluateCondition(ctx, r.Condition))
            {
                return ApplyAction(ctx, r.Action, isDownload);
            }
        }

        return AMScriptResult.NoChange(ctx);
    }

    private bool EvaluateCondition(AMScriptContext ctx, string cond)
    {
        cond = cond.Trim();

        // Basic OR
        var orParts = cond.Split("||", StringSplitOptions.RemoveEmptyEntries);
        foreach (var orPart in orParts)
        {
            if (EvaluateAndPart(ctx, orPart))
                return true;
        }

        return false;
    }

    private bool EvaluateAndPart(AMScriptContext ctx, string cond)
    {
        var andParts = cond.Split("&&", StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in andParts)
        {
            if (!EvaluateAtomic(ctx, part.Trim()))
                return false;
        }

        return true;
    }

    private bool EvaluateAtomic(AMScriptContext ctx, string atom)
    {
        // !expr
        if (atom.StartsWith("!"))
            return !EvaluateAtomic(ctx, atom[1..].Trim());

        // Equality
        if (atom.Contains("=="))
        {
            var p = atom.Split("==", 2);
            return GetValue(ctx, p[0].Trim())
                   == GetValue(ctx, p[1].Trim());
        }

        if (atom.Contains("!="))
        {
            var p = atom.Split("!=", 2);
            return GetValue(ctx, p[0].Trim())
                   != GetValue(ctx, p[1].Trim());
        }

        // Boolean symbol
        return GetValue(ctx, atom).ToLower() == "true";
    }

    private string GetValue(AMScriptContext ctx, string token)
    {
        token = token.Trim();

        // String literal
        if (token.StartsWith("\"") && token.EndsWith("\""))
            return token.Trim('"');

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
            _ => ""
        };
    }

}