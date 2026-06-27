using System.Diagnostics;

namespace Toolkit.Runner.Diagnostics;

/// <summary>
/// Routes WPF data-binding trace errors into the runner log. A button that "does nothing"
/// is often a failed <c>{Binding Command}</c> — this surfaces that instead of swallowing it.
/// </summary>
public sealed class BindingErrorListener : TraceListener
{
    public static void Install()
    {
        PresentationTraceSources.Refresh();
        var source = PresentationTraceSources.DataBindingSource;
        source.Listeners.Add(new BindingErrorListener());
        source.Switch.Level = SourceLevels.Error | SourceLevels.Warning;
    }

    public override void Write(string? message) { }

    public override void WriteLine(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            Logger.Error($"Binding: {message}");
    }
}
