namespace AutoSettings.Sdk;

/// <summary>Version information about this SDK.</summary>
public static class SdkInfo
{
    /// <summary>
    /// The SDK version (semantic versioning). Plugins declare the major version they were built for in
    /// <c>plugin.yaml</c> (<c>sdk: "1.0"</c>). AutoSettings loads plugins whose SDK major version it supports.
    /// </summary>
    public const string Version = "1.0.0";

    /// <summary>The major SDK version. A change here means plugins must be rebuilt.</summary>
    public const int MajorVersion = 1;

    /// <summary>
    /// Version of the protocol AutoSettings uses to talk to a plugin host. Plugins never use it directly; it is
    /// exchanged when a plugin starts so that mismatched versions fail with a clear message.
    /// </summary>
    public const int ProtocolVersion = 1;
}
