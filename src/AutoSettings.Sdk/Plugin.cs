namespace AutoSettings.Sdk;

/// <summary>
/// The entry point of a .NET plugin. AutoSettings creates one instance of the class that implements it (the class
/// needs a public parameterless constructor) and calls <see cref="Configure"/> once when the plugin starts.
/// </summary>
/// <example>
/// <code>
/// public sealed class UsbPlugin : IPlugin
/// {
///     public void Configure(IPluginBuilder builder) =>
///         builder.AddAction(new EjectAction()).AddTrigger(new DriveWatcher());
/// }
/// </code>
/// </example>
public interface IPlugin
{
    /// <summary>Registers the plugin's actions, conditions and triggers.</summary>
    /// <param name="builder">Collects the components.</param>
    void Configure(IPluginBuilder builder);
}

/// <summary>Collects the components of a plugin in <see cref="IPlugin.Configure"/>.</summary>
/// <remarks>
/// Each component class must have a <see cref="PluginComponentAttribute"/> that names its type, for example
/// <c>acme.usb.eject</c>. The type must start with the plugin id from <c>plugin.yaml</c>.
/// </remarks>
public interface IPluginBuilder
{
    /// <summary>Adds an action. Implement <see cref="IRevertiblePluginAction"/> to let profiles undo it.</summary>
    /// <param name="action">The action.</param>
    /// <returns>The builder, for chaining.</returns>
    IPluginBuilder AddAction(IPluginAction action);

    /// <summary>Adds a condition.</summary>
    /// <param name="condition">The condition.</param>
    /// <returns>The builder, for chaining.</returns>
    IPluginBuilder AddCondition(IPluginCondition condition);

    /// <summary>Adds a trigger: code that watches for something and raises plugin events.</summary>
    /// <param name="trigger">The trigger.</param>
    /// <returns>The builder, for chaining.</returns>
    IPluginBuilder AddTrigger(IPluginTrigger trigger);
}

/// <summary>An action: changes something when an automation runs or a profile is applied.</summary>
public interface IPluginAction
{
    /// <summary>
    /// Runs the action. Throw <see cref="PluginActionException"/> with a message for the user when it fails;
    /// other exceptions are reported as unexpected errors.
    /// </summary>
    /// <param name="request">The parameters and what started the action.</param>
    /// <param name="cancellationToken">Cancelled when the app stops or the action takes too long.</param>
    Task ExecuteAsync(ActionRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// An action that profiles can undo. Before a profile changes a setting for the first time, AutoSettings calls
/// <see cref="CaptureAsync"/> to save the current value; when the profile ends it calls <see cref="RestoreAsync"/>
/// with that value.
/// </summary>
/// <remarks>
/// Settings are told apart by the fields marked <see cref="FieldAttribute.KeyField"/> (for example the drive
/// letter), so two actions with different key values change different settings.
/// </remarks>
public interface IRevertiblePluginAction : IPluginAction
{
    /// <summary>Reads the current value of the setting that <paramref name="request"/> would change.</summary>
    /// <param name="request">The parameters of the action that is about to run.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The value as text (any format you like), or <c>null</c> when there is nothing to restore.</returns>
    Task<string?> CaptureAsync(ActionRequest request, CancellationToken cancellationToken);

    /// <summary>Puts the setting back to a value returned by <see cref="CaptureAsync"/>.</summary>
    /// <param name="request">The parameters of the action that changed the setting.</param>
    /// <param name="snapshot">The value returned by <see cref="CaptureAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task RestoreAsync(ActionRequest request, string? snapshot, CancellationToken cancellationToken);
}

/// <summary>A condition: must be true for an automation to run.</summary>
public interface IPluginCondition
{
    /// <summary>Checks the condition. It should be quick (well under a second); exceptions count as false.</summary>
    /// <param name="request">The parameters and the event being handled.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Whether the condition is met.</returns>
    ValueTask<bool> EvaluateAsync(ConditionRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// A trigger source: runs while the plugin is loaded and raises events with <see cref="ITriggerSink.Raise"/>.
/// Automations react to them through the trigger types the class declares.
/// </summary>
/// <remarks>
/// Put one <see cref="PluginComponentAttribute"/> on the class for every event it raises, for example
/// <c>acme.usb.connected</c> and <c>acme.usb.disconnected</c>. Each becomes a trigger users can pick. The
/// <see cref="FieldAttribute"/>s of the class apply to all of them.
/// </remarks>
public interface IPluginTrigger
{
    /// <summary>
    /// Watches for events until <paramref name="cancellationToken"/> is cancelled. Do not return early: when this
    /// method ends, the trigger stops.
    /// </summary>
    /// <param name="sink">Raises events.</param>
    /// <param name="cancellationToken">Cancelled when the plugin stops.</param>
    Task RunAsync(ITriggerSink sink, CancellationToken cancellationToken);
}

/// <summary>Raises plugin events.</summary>
public interface ITriggerSink
{
    /// <summary>Raises an event. Automations whose trigger has this event name run.</summary>
    /// <param name="eventName">The event, for example <c>acme.usb.connected</c>. It must start with the plugin id.</param>
    /// <param name="data">
    /// Values sent with the event, for example <c>drive = E:</c>. Trigger fields with the same name filter on them,
    /// and actions can use them as <c>{{ event.data.drive }}</c>.
    /// </param>
    /// <param name="user">The user the event is about, if any. User triggers (<c>user:</c>) filter on it.</param>
    void Raise(string eventName, IReadOnlyDictionary<string, string>? data = null, PluginUser? user = null);

    /// <summary>Writes to the activity log.</summary>
    IPluginLog Log { get; }
}

/// <summary>Writes to the AutoSettings activity log (the Activity page).</summary>
public interface IPluginLog
{
    /// <summary>Something normal happened.</summary>
    /// <param name="message">Plain-language message.</param>
    void Info(string message);

    /// <summary>Something the user may want to know about.</summary>
    /// <param name="message">Plain-language message.</param>
    void Warning(string message);

    /// <summary>Something went wrong.</summary>
    /// <param name="message">Plain-language message.</param>
    void Error(string message);
}

/// <summary>Thrown by an action to report a failure the user can understand, for example "drive E: is not connected".</summary>
public class PluginActionException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What went wrong, in plain language. It is shown in the activity log.</param>
    public PluginActionException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with the error that caused it.</summary>
    /// <param name="message">What went wrong, in plain language.</param>
    /// <param name="innerException">The underlying error.</param>
    public PluginActionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
