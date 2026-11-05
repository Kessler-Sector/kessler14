namespace Content.Server._Kessler.Persistence;

/// <summary>
/// Raised (broadcast) right before the persistent sector is serialized, so systems that keep state in memory can write
/// it onto saved entities first.
/// </summary>
public sealed class BeforeSectorSaveEvent : EntityEventArgs;
