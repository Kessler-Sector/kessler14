using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Holiday;
using Content.Server.Maps;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map.Events;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Kessler.Persistence;

/// <summary>
/// Loads saves of a running sector.
/// </summary>
/// <remarks>
/// Map migrations (<c>migration.yml</c>, <c>nf_migration.yml</c>) and holiday replacements rename or delete
/// prototypes whenever a file is read. They are written for map files, and many of them target prototypes that still
/// exist (Frontier replacing upstream items, holiday variants). Applied to a save of live entities, they would replace
/// or delete those entities on every restart. While loading a save, only entries whose source prototype no longer
/// exists are kept, so renamed and removed prototypes are still migrated.
/// </remarks>
public sealed class PersistenceLoadSystem : EntitySystem
{
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private bool _loadingSave;

    /// <summary>
    /// Migration entries skipped during the last save load, because their source prototype still exists.
    /// </summary>
    public int SkippedMigrations { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BeforeEntityReadEvent>(OnBeforeEntityRead,
            after: new[] { typeof(MapMigrationSystem), typeof(HolidaySystem) });
    }

    /// <summary>
    /// Loads a save file without applying migrations to prototypes that still exist.
    /// </summary>
    public bool TryLoadSave(ResPath path, MapLoadOptions options, [NotNullWhen(true)] out LoadResult? result)
    {
        _loadingSave = true;
        SkippedMigrations = 0;
        try
        {
            return _mapLoader.TryLoadGeneric(path, out result, options);
        }
        finally
        {
            _loadingSave = false;
        }
    }

    private void OnBeforeEntityRead(BeforeEntityReadEvent ev)
    {
        if (!_loadingSave)
            return;

        foreach (var id in ev.RenamedPrototypes.Keys.ToArray())
        {
            if (!_proto.HasIndex<EntityPrototype>(id))
                continue;

            ev.RenamedPrototypes.Remove(id);
            SkippedMigrations++;
        }

        SkippedMigrations += ev.DeletedPrototypes.RemoveWhere(id => _proto.HasIndex<EntityPrototype>(id));
    }
}
