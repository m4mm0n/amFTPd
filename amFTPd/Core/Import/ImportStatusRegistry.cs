namespace amFTPd.Core.Import;

/// <summary>
/// Provides a global registry for tracking the progress of an ongoing import operation.
/// </summary>
/// <remarks>This static class manages a single import progress instance at a time. It is intended for scenarios
/// where only one import operation is tracked globally. Access to the current progress is not thread-safe.</remarks>
public static class ImportProgressRegistry
{
    private static ImportProgress? _current;

    public static ImportProgress? Current => _current;

    public static ImportProgress Start(string name, int total)
    {
        _current = new ImportProgress
        {
            Name = name,
            Total = total
        };
        return _current;
    }

    public static void Finish()
        => _current = null;

    public static void Cancel()
    {
        if (_current != null)
            _current.CancelRequested = true;
    }
}