using AutoSettings.Core.Engine;
using AutoSettings.Core.Model;
using Microsoft.Win32;

namespace AutoSettings.Platform.Actions;

/// <summary>Base class for actions that run synchronously.</summary>
public abstract class SyncActionHandler : IActionHandler
{
    /// <inheritdoc />
    public abstract string Type { get; }

    /// <inheritdoc />
    public Task ExecuteAsync(ComponentConfig action, ActionContext context, CancellationToken cancellationToken)
    {
        Execute(action, context);
        return Task.CompletedTask;
    }

    /// <summary>Runs the action.</summary>
    protected abstract void Execute(ComponentConfig action, ActionContext context);
}

/// <summary>Base class for revertible actions that run synchronously.</summary>
public abstract class SyncRevertibleActionHandler : SyncActionHandler, IRevertibleActionHandler
{
    /// <inheritdoc />
    public Task<string?> CaptureAsync(ComponentConfig action, ActionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(Capture(action, context));

    /// <inheritdoc />
    public Task RestoreAsync(ComponentConfig action, string? snapshot, ActionContext context, CancellationToken cancellationToken)
    {
        Restore(action, snapshot, context);
        return Task.CompletedTask;
    }

    /// <summary>Returns the current value of the setting.</summary>
    protected abstract string? Capture(ComponentConfig action, ActionContext context);

    /// <summary>Restores a value returned by <see cref="Capture"/>.</summary>
    protected abstract void Restore(ComponentConfig action, string? snapshot, ActionContext context);
}

/// <summary>Small registry helpers.</summary>
internal static class Reg
{
    public static int GetDword(RegistryKey key, string name, int fallback) =>
        key.GetValue(name) is int value ? value : fallback;

    public static void SetDword(RegistryKey key, string name, int value) =>
        key.SetValue(name, value, RegistryValueKind.DWord);
}
