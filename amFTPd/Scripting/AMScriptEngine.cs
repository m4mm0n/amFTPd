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

    /// <summary>
    /// Gets or sets the delegate used to log debug messages.
    /// </summary>
    /// <remarks>Use this property to provide a custom logging mechanism for debug messages.  Assign a
    /// delegate that processes or outputs the debug messages as needed.</remarks>
    public Action<string>? DebugLog { get; set; }
    /// <summary>
    /// Initializes a new instance of the <see cref="AMScriptEngine"/> class with the specified script file path.
    /// </summary>
    /// <remarks>The constructor loads the script file and sets up a file watcher to monitor changes to the
    /// specified file. Ensure the provided file path is valid and accessible.</remarks>
    /// <param name="filePath">The path to the script file to be loaded and monitored. Cannot be null or empty.</param>
    public AMScriptEngine(string filePath)
    {
        _filePath = filePath;
        Load();
        Watch();
    }

    // ---------------------------------------------------------
    // Load + Watch
    // ---------------------------------------------------------
    /// <summary>
    /// Loads rules from the specified file and populates the internal rule collection.
    /// </summary>
    /// <remarks>This method reads the file located at the path specified by <c>_filePath</c>. Each line in
    /// the file is processed to extract rules in the format <c>if(condition) action;</c>. Lines that are empty, start
    /// with a comment character (<c>#</c>), or do not conform to the expected format are ignored. If the file does not
    /// exist, the method logs a message and exits without modifying the rule collection.</remarks>
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

            var start = line.IndexOf('(');
            var end = line.IndexOf(')');
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
    /// <summary>
    /// Evaluates the download operation within the specified context.
    /// </summary>
    /// <param name="ctx">The context in which the download evaluation is performed. This parameter cannot be null.</param>
    /// <returns>An <see cref="AMScriptResult"/> representing the result of the evaluation.</returns>
    public AMScriptResult EvaluateDownload(AMScriptContext ctx)
        => EvaluateInternal(ctx);
    /// <summary>
    /// Evaluates the provided upload context and returns the result of the evaluation.
    /// </summary>
    /// <param name="ctx">The context of the upload to be evaluated, containing relevant data and parameters.</param>
    /// <returns>An <see cref="AMScriptResult"/> representing the outcome of the evaluation.</returns>
    public AMScriptResult EvaluateUpload(AMScriptContext ctx)
        => EvaluateInternal(ctx);
    /// <summary>
    /// Evaluates the user within the specified AMScript context and returns the result of the evaluation.
    /// </summary>
    /// <param name="ctx">The <see cref="AMScriptContext"/> containing the context information for the evaluation. This parameter cannot
    /// be null.</param>
    /// <returns>An <see cref="AMScriptResult"/> representing the outcome of the evaluation.</returns>
    public AMScriptResult EvaluateUser(AMScriptContext ctx) 
        => EvaluateInternal(ctx);

    // Generic: we treat download/upload same in v1, context decides meaning
    private AMScriptResult EvaluateInternal(AMScriptContext ctx)
    {
        foreach (var rule in _rules.Where(rule => EvaluateCondition(ctx, rule.Condition)))
            return ApplyAction(ctx, rule.Action);

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
        return orParts.Any(or => EvaluateAndPart(ctx, or));
    }

    private bool EvaluateAndPart(AMScriptContext ctx, string cond)
    {
        var andParts = cond.Split(new[] { "&&" }, StringSplitOptions.RemoveEmptyEntries);
        return andParts.All(part => EvaluateAtomic(ctx, part.Trim()));
    }

    private bool EvaluateAtomic(AMScriptContext ctx, string atom)
    {
        atom = atom.Trim();
        if (atom.Length == 0)
            return false;

        // Handle unary NOT
        if (atom[0] == '!')
            return !EvaluateAtomic(ctx, atom[1..].Trim());

        string op = null!;
        string left = null!, right = null!;

        // Determine operator
        if (atom.Contains("==", StringComparison.Ordinal))
        {
            op = "==";
        }
        else if (atom.Contains("!=", StringComparison.Ordinal))
        {
            op = "!=";
        }
        else
        {
            op = "bool";
        }

        switch (op)
        {
            case "==":
            {
                var parts = atom.Split(new[] { "==" }, 2, StringSplitOptions.None);
                left = parts[0].Trim();
                right = parts[1].Trim();
                return GetValue(ctx, left) == GetValue(ctx, right);
            }

            case "!=":
            {
                var parts = atom.Split(new[] { "!=" }, 2, StringSplitOptions.None);
                left = parts[0].Trim();
                right = parts[1].Trim();
                return GetValue(ctx, left) != GetValue(ctx, right);
            }

            case "bool":
            default:
                // Treat variable token as boolean
                var v = GetValue(ctx, atom);
                return v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
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

        var cost = ctx.CostDownload;
        var earn = ctx.EarnedUpload;
        string? msg = null;

        // single first token
        var first = action.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var keyword = first.Length > 0 ? first[0].ToLowerInvariant() : string.Empty;
        var rest = first.Length > 1 ? first[1] : string.Empty;

        switch (keyword)
        {
            case "return":
                {
                    // handle the "return ____" subcommands
                    var parts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var sub = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
                    var tail = parts.Length > 1 ? parts[1] : string.Empty;

                    switch (sub)
                    {
                        case "allow":
                            return AMScriptResult.Allow(ctx);

                        case "deny":
                            // return deny "message"
                            msg = tail.Trim().Trim('"');
                            return AMScriptResult.DenyWithReason(msg);

                        case "output":
                            msg = tail.Trim().Trim('"');
                            return AMScriptResult.CustomOutput(msg);

                        case "override":
                            return AMScriptResult.SiteOverride();

                        case "section":
                            var sectionName = tail.Trim().Trim('"');
                            return new AMScriptResult(
                                AMRuleAction.Allow,
                                ctx.CostDownload,
                                ctx.EarnedUpload,
                                Message: $"SECTION_OVERRIDE::{sectionName}"
                            );

                        case "set_dl":
                            var dl = int.Parse(tail);
                            return new AMScriptResult(
                                AMRuleAction.Allow,
                                ctx.CostDownload,
                                ctx.EarnedUpload,
                                NewDownloadLimit: dl
                            );

                        case "set_ul":
                            var ul = int.Parse(tail);
                            return new AMScriptResult(
                                AMRuleAction.Allow,
                                ctx.CostDownload,
                                ctx.EarnedUpload,
                                NewUploadLimit: ul
                            );

                        case "add_credits":
                            var add = long.Parse(tail);
                            return new AMScriptResult(
                                AMRuleAction.Allow,
                                ctx.CostDownload,
                                ctx.EarnedUpload,
                                CreditDelta: add
                            );

                        case "sub_credits":
                            var subc = long.Parse(tail);
                            return new AMScriptResult(
                                AMRuleAction.Allow,
                                ctx.CostDownload,
                                ctx.EarnedUpload,
                                CreditDelta: -subc
                            );
                    }

                    break;
                }

            case "cost_download":
                ParseNumericAssignment(action, ref cost);
                break;

            case "earned_upload":
                ParseNumericAssignment(action, ref earn);
                break;

            case "log":
                var body = action["log".Length..].Trim();
                if (body.Length >= 2 && body[0] == '"' && body[^1] == '"')
                    msg = body[1..^1];
                DebugLog?.Invoke($"[AMScript] {msg}");
                break;
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