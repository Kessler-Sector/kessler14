cmd-persistenceroundtrip-desc = Saves a map, loads it into a new paused map and reports what changed. Dev servers only: restart afterwards.
cmd-persistenceroundtrip-help = Usage: {$command} [mapId] [keep] [enginedefault]
    mapId: map to test, defaults to the sector map.
    keep: keep the loaded copy (paused) instead of deleting it.
    enginedefault: skip prototypes marked save: false (such as mobs) like the engine does, instead of the Kessler save policy.
cmd-persistenceroundtrip-hint = [mapId] [keep] [enginedefault]
cmd-persistenceroundtrip-bad-arg = Unknown argument: {$arg}
cmd-persistenceroundtrip-no-map = Map {$map} does not exist.
cmd-persistenceroundtrip-start = Running persistence round trip on {$map}. The server will freeze until it finishes.
cmd-persistenceroundtrip-failed = Round trip failed: {$reason}
cmd-persistenceroundtrip-summary-entities = Entities: {$live} live, {$file} in file, {$loaded} loaded.
cmd-persistenceroundtrip-summary-timings = Serialize {$serialize}s, write {$write}s, load {$load}s, file {$size} MiB.
cmd-persistenceroundtrip-summary-diffs = {$values} value differences, {$papers} changed papers, {$unsaveable} unsaved entities, {$dropped} entities dropped inside them.
cmd-persistenceroundtrip-summary-log = Errors logged: {$saveErrors} during save, {$loadErrors} during load.
cmd-persistenceroundtrip-kept = Loaded copy kept as map {$map}.
cmd-persistenceroundtrip-report = Full report: {$path} (in the server data folder).
