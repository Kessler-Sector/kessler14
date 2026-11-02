using Robust.Shared.Player;

namespace Content.Server._WF.GameTicking;

/// <summary>
///     Raised by <see cref="Content.Server.GameTicking.GameTicker.MakeJoinGame"/> before anything is spawned.
///     Cancel it to stop the join, for example to ask the player something first.
/// </summary>
public sealed class PlayerJoinAttemptEvent(ICommonSession player, EntityUid station, string? job) : CancellableEntityEventArgs
{
    public readonly ICommonSession Player = player;
    public readonly EntityUid Station = station;
    public readonly string? Job = job;
}
