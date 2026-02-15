using System;
using System.Collections.ObjectModel;

namespace Espmon;

internal sealed class OpenFlashProgressReporter : IOpenFlashProgress
{
    readonly ObservableCollection<string> _log;
    public OpenFlashProgressReporter(ObservableCollection<string> log)
    {
        _log = log;
    }

    public void Report(FlashProgressEntry value)
    {
        var action = value.Action;
        int progress = value.Progress;
        if (progress > -1)
        {
            if (_log.Count == 0 || !_log[_log.Count - 1].StartsWith(action + " ", StringComparison.Ordinal))
            {
                _log.Add($"{action} {progress}%");
            }
            else
            {
                _log[_log.Count - 1] = ($"{action} {progress}%");
            }
        } else
        {
            if (_log.Count == 0 || !_log[_log.Count - 1].StartsWith(action, StringComparison.Ordinal))
            {
                _log.Add($"{action}");
            }
            else
            {
                _log[_log.Count - 1] = _log[_log.Count - 1] + ".";
            }
        }
    }
}
