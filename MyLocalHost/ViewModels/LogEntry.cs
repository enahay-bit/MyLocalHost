namespace MyLocalHost.ViewModels;

/// <summary>ログ欄の1行。</summary>
public sealed record LogEntry(DateTime Time, string Site, string Text, bool IsError)
{
    public string TimeText => Time.ToString("HH:mm:ss");
}
