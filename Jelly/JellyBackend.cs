using System;

using Jelly.GameContent;

namespace Jelly;

public static class JellyBackend
{
    private static bool _initialized;

    internal static Random IDRandom = new();

    internal static Logger Logger = new("Jelly");

    private static ContentProvider _contentProvider;

    public static ContentProvider ContentProvider
    {
        get {
            CheckInitialized();
            return _contentProvider;
        }
    }

    public static bool DrawingEnabled => ContentProvider is not null;

    public static bool DebugEnabled { get; set; }

    /// <summary>
    /// This method should called before calling any other update methods related to <see cref="Jelly"/>
    /// </summary>
    /// <param name="deltaTimeThisFrame">The current <i>unscaled</i> (real time) time in seconds since the last frame</param>
    public static void PreUpdate(float deltaTimeThisFrame)
    {
        Time.SetDeltaTime(deltaTimeThisFrame);
    }

    /// <summary>
    /// This method should called after calling most other update methods related to <see cref="Jelly"/>
    /// </summary>
    /// <param name="deltaTimeThisFrame">The current <i>unscaled</i> (real time) time in seconds since the last frame</param>
    public static void PostUpdate(float deltaTimeThisFrame)
    {
        SceneManager.ChangeSceneImmediately(SceneManager.nextScene);
    }

    /// <summary>
    /// Sets up the main providers and engine backend utilities. Call once at program start.
    /// </summary>
    public static void Initialize(ContentProvider contentProvider)
    {
        ArgumentNullException.ThrowIfNull(contentProvider, nameof(contentProvider));

        if(_initialized) throw new InvalidOperationException(nameof(Initialize) + " cannot be called more than once!");
        _initialized = true;

        _contentProvider = contentProvider;

        Registries.Init();
    }

    private static void CheckInitialized()
    {
        if(!_initialized)
        {
            throw new InvalidOperationException(nameof(Initialize) + " has not been called yet!");
        }
    }
}
