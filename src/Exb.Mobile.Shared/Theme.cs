namespace Exb.Mobile.Shared;

/// <summary>Maps a session's Kind string to an (icon, CSS colour token) pair — used consistently on every session tile and the detail screen.</summary>
public static class SessionKindStyle
{
    public static (string Icon, string ColorVar) For(string? kind) => kind?.ToLowerInvariant() switch
    {
        "meeting" => ("groups", "--tertiary"),
        "workshop" => ("build", "--secondary"),
        "panel" => ("forum", "--primary"),
        "demo" => ("play_circle", "--tertiary"),
        "ceremony" => ("celebration", "--error"),
        _ => ("record_voice_over", "--primary"),
    };
}
