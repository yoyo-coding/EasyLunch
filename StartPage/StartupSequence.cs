using System;

namespace StartPage;

public sealed class StartupSequence
{
    private readonly Action _configureWindow;
    private readonly Action _applyBackdrop;
    private readonly Action _onLoaded;
    private bool _isLoaded;

    public StartupSequence(Action configureWindow, Action applyBackdrop, Action onLoaded)
    {
        _configureWindow = configureWindow ?? throw new ArgumentNullException(nameof(configureWindow));
        _applyBackdrop = applyBackdrop ?? throw new ArgumentNullException(nameof(applyBackdrop));
        _onLoaded = onLoaded ?? throw new ArgumentNullException(nameof(onLoaded));
    }

    public void OnConstruct()
    {
        _isLoaded = false;
    }

    public void OnLoaded()
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        _configureWindow();
        _applyBackdrop();
        _onLoaded();
    }

    public bool IsLoaded => _isLoaded;
}
