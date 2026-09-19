namespace Capsule.Runtime;

/// <summary>
/// The graphics backend's own handle for the game window, which a platform module passes back to
/// whatever windowing library that backend is built on. Capsule never interprets it, and it is not the
/// operating system's handle. On the shipped desktop backend it is an <c>SDL_Window*</c>.
/// </summary>
/// <param name="Value">The handle as the backend reports it. Zero before a window exists.</param>
public readonly record struct WindowHandle(nint Value);
