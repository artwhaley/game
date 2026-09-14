# Unity SQLite provider — vendoring record

Ticket 00 for Conversation Performance V1. Records the provider decision, the
exact artifacts committed, the shared-source arrangement that puts
`GameContentSnapshotLoader` inside Unity, and the evidence for each. Referenced
by [BUILD-PLAN.md](BUILD-PLAN.md) and
[TICKET-STACK.md](../../Tickets/UnityPerformanceExecutionStack/TICKET-STACK.md) ticket 00.

## Decision

Unity uses **Microsoft.Data.Sqlite.Core 10.0.11 + SQLitePCLRaw 2.1.12**
(vendored under `Assets/Plugins/`), called with an explicit
`SQLitePCL.Batteries_V2.Init()`.

This is the same provider pair the DotNet loader tests already exercise, so
Unity and WPF share one SQLite engine version and one `DbConnection` contract.
`Microsoft.Data.Sqlite.Core` ships a **netstandard2.0** build, which is what
makes it usable under Unity's scripting profile.

## Rejected alternative: SQLite4Unity3d

Considered first and inspected directly (`SQLite4Unity3d.zip`, last commit
2019). It was rejected on evidence:

| Finding | Consequence |
|---|---|
| Zip contains only 5 natives: Windows `sqlite3.dll` (x86, x64, WSA ARM/x64/x86) and Android `libsqlite3.so` (armeabi-v7a, x86, arm64-v8a) | No macOS, iOS, or Linux natives at all, despite the README claiming iOS/Mac support. iOS additionally needs a static lib, not a dylib. |
| Zip ships no managed provider — only `SQLite.cs` (sqlite-net ORM) | `GameContentSnapshotLoader.Load(DbConnection, bool)` would still need a new ~600-line ADO.NET adapter over an ORM. |
| Newest native 2019, ORM 2017; sqlite-net carries a *custom* license | Stale, and a license review would be required before adoption anyway. |

Chromium of `__MACOSX/` entries in that archive are AppleDouble resource-fork
junk, not binaries.

## Vendored files

Copied from the local NuGet cache — the cache pinned by
`DotNet/Game.Content.Sqlite.Tests` — so versions match the DotNet side exactly.

| Destination | Source | Version |
|---|---|---|
| `Assets/Plugins/SQLitePCLRaw/Microsoft.Data.Sqlite.dll` | `microsoft.data.sqlite.core/10.0.11/lib/netstandard2.0/` | 10.0.11 |
| `Assets/Plugins/SQLitePCLRaw/SQLitePCLRaw.core.dll` | `sqlitepclraw.core/2.1.12/lib/netstandard2.0/` | 2.1.12 |
| `Assets/Plugins/SQLitePCLRaw/SQLitePCLRaw.batteries_v2.dll` | `sqlitepclraw.bundle_e_sqlite3/2.1.12/lib/netstandard2.0/` | 2.1.12 |
| `Assets/Plugins/SQLitePCLRaw/SQLitePCLRaw.provider.e_sqlite3.dll` | `sqlitepclraw.provider.e_sqlite3/2.1.12/lib/netstandard2.0/` | 2.1.12 |
| `Assets/Plugins/x86_64/e_sqlite3.dll` | `sqlitepclraw.lib.e_sqlite3/2.1.12/runtimes/win-x64/native/` | 2.1.12 |
| `Assets/Plugins/x86/e_sqlite3.dll` | `sqlitepclraw.lib.e_sqlite3/2.1.12/runtimes/win-x86/native/` | 2.1.12 |

`*.dll` is already a Git LFS pattern in `.gitattributes`; no gitattributes
change was needed.

`Assets/Tests/EditMode/TruthCardGame.Tests.EditMode.asmdef` declares the four
managed assemblies in `precompiledReferences`, because that assembly sets
`overrideReferences: true` and would otherwise see only `nunit.framework.dll`.

## Cross-platform matrix

`SQLitePCLRaw.lib.e_sqlite3` ships natives for the platforms below. Only the
Windows pair is vendored so far, because nothing else can be exercised yet and
each additional platform needs its plugin platform/CPU settings confirmed in the
editor Inspector rather than guessed.

| Target | Native present in the package | Vendored |
|---|---|---|
| Windows x86 / x64 | `e_sqlite3.dll` | yes |
| Windows arm64 | `e_sqlite3.dll` | no |
| macOS x64 / arm64 | `e_sqlite3.dylib` | no |
| Linux x64 / arm64 | `libe_sqlite3.so` | no |
| Android / iOS | separate `SQLitePCLRaw.lib.e_sqlite3.android` / `.ios` packages | no |

Adding macOS and Linux is a copy plus an Inspector check. Android and iOS need
their own packages restored first.

## Licensing

| Component | License |
|---|---|
| Microsoft.Data.Sqlite | MIT |
| SQLitePCLRaw | Apache-2.0 |
| SQLite (e_sqlite3) | Public domain |

Before any standalone or mobile build is adopted, `TESTING-METHODOLOGY.md` §12.1
requires the full platform proof: headless compiles, editor and standalone
execution, IL2CPP, native placement per architecture, assembly-definition
references, stripping/linker preservation, packaged read-only data access and
clean failure on a missing or locked database. None of that is claimed yet.

## Verified results

Run 2026-09-11 against the pinned Unity 6000.5.9f1 through
`scripts/run-unity-tests.sh`:

| Run | Result |
|---|---|
| `--filter SqliteProviderSmokeTests` | 3/3 passed |
| `--filter SqliteContentIntegrationTests` | 6/6 passed |
| Full `Assets/Tests/EditMode` suite | 29/29 passed |
| `dotnet test Game.Workbench.sln` | 401/401 passed |

Recorded from the run log: e_sqlite3 engine version **3.53.3**, canonical
content schema version **11**. Evidence is the `Logs/Ticket00-EditorSmoke-*`,
`Logs/Ticket00-LoaderIntegration-*` and `Logs/Ticket00-FullEditMode-*` XML/log
pairs.

Proven by those runs: the vendored provider loads its native engine inside
Unity; the provider is a `DbConnection`; `@name` parameter binding, explicit
transactions and rollback behave correctly; the connection releases its file
handle on dispose; and the canonical `Content/GameContent.db` opens read-only,
passes `integrity_check` and `foreign_key_check`, and rejects a write.

## Connection requirement for the Unity read path

**`Pooling=False` is required** on connections Unity opens against canonical
content. Microsoft.Data.Sqlite pools connections by default, and a pooled
connection keeps the database file handle open after `Dispose`.

This is not theoretical: it failed the first run of the smoke test, where the
disposable database could not be deleted after the connection was disposed
(`IOException: The process cannot access the file ... because it is being used
by another process`). Pooling left on would keep `Content/GameContent.db` held
open after a Unity read — exactly what the contract forbids before playback and
on Repeat. Any loader wiring must set it, and the smoke test now asserts the
release explicitly rather than trusting it.

## Shared source arrangement (ticket 00)

Ticket 00's remaining acceptance was `GameContentSnapshotLoader.Load(connection,
ensureSchema: false)` running in Unity with exactly one CLR identity per
`TruthCardGame.Content`/`Core` type. That needed two decisions.

### 1. Where the mapping source lives

Unity can only compile files under `Assets/`, and importing a compiled
`Game.Content.Sqlite.dll` alongside Unity's portable sources would create
competing type identities. So the mapping source moved to
`Assets/Scripts/Portable/Game.Content.Sqlite/` and the project stayed put,
linked like `Game.Content`/`Game.Core`:

| Piece | Location |
|---|---|
| Shared source (40 `.cs`: loader, repositories, migrations, commands) | `Assets/Scripts/Portable/Game.Content.Sqlite/` |
| Unity assembly | `Game.Content.Sqlite.asmdef` (references `Game.Content`, `Game.Core`; `noEngineReferences: true`) |
| DotNet project (unchanged path) | `DotNet/Game.Content.Sqlite/Game.Content.Sqlite.csproj` |

The csproj sets `EnableDefaultCompileItems=false` and globs the Assets sources,
so there is one physical copy of every file and no second loader. `TruthCardGame`
and `TruthCardGame.Tests.EditMode` now reference the new assembly. The rest of
the DotNet solution (`Game.Content.Sqlite.Tests`, `Game.ReferenceHost.Wpf`,
`Game.Content.Sqlite.Tool`) is untouched because it references the project, and
all 401 DotNet tests still pass — the arrangement did not break any source or
project reference.

### 2. Where the schema scripts live

`Game.Content.Sqlite` reads its 11 migration scripts via
`Assembly.GetManifestResourceStream`, and a Unity-compiled assembly embeds no
resources, so `CoreMigrations` could never have run in Unity as written.

| Piece | Location |
|---|---|
| Canonical scripts (one copy) | `Assets/StreamingAssets/GameContentSchema/*.sql` |
| DotNet reader | `EmbeddedResource` in the csproj, resource names `TruthCardGame.Content.Sqlite.<file>` |
| Unity reader | `Assets/Scripts/Game/UnitySchemaScripts.cs` → `SchemaScripts.Reader` |

`Assets/StreamingAssets` is the only place Unity can read raw non-source files
from, verbatim at edit time and in a build, so it is the single physical copy
rather than a generated C# duplicate or a renamed `.txt` twin. `SchemaScripts`
resolves a script by trying the host-installed reader first and the embedded
resources second, and **throws with the remedy named** if neither works — there
is deliberately no path that returns an empty script, because an empty script
would still "succeed" and leave a half-created database.

`CoreMigration` now carries its `ScriptFileName` and resolves the text lazily on
first access, and `CoreMigrations.All` is lazy too. That means declaring a
migration and inspecting `MaxVersion` never read SQL at all, which is what lets a
read-only Unity host check the stored schema version with no scripts available.

### Evidence from the Unity suite

| Test | What it proves |
|---|---|
| `CanonicalDatabase_LoadsThroughTheSharedLoader_ReadOnly` | The loader reads the canonical DB read-only in Unity (schema v11), the connection rejects writes, and every present session/phase/card is structurally complete |
| `MigratedLegacyDatabase_LoadsInUnity_AndDrivesGameSessionEngine` | Migrate the v1 fixture with the Unity-side scripts from v1 to **v11**, load it through the shared loader, hand the returned `GameContentDefinition` **directly** into `new GameSessionEngine(...)`, and run it to `SessionCompleted` — the log records `cardsDrawn=30`. Then delete the file, proving handle release |
| `Loader_RunsInsideACallerOwnedReadTransaction_AndLeavesNothingBehind` | The loader works inside a caller-owned transaction (so a host can hold one consistent view across its many queries); rollback leaves integrity, foreign keys and schema version untouched, and counts stable |
| `ContentAndCoreTypes_ResolveToExactlyOneDefinition_AcrossLoadedAssemblies` | Reflection scan over every loaded assembly: no duplicate full name in `TruthCardGame.Content(.Sqlite)`/`TruthCardGame.Core`, no `ReflectionTypeLoadException`, exactly one assembly per portable assembly name, and no such assembly loaded from `Plugins/` |
| `LoaderReturnType_AndEngineParameterType_AreTheUnityGameContentDefinition` | The loader's declared return type, the engine constructor parameter type and `typeof(GameContentDefinition)` are the same `Type` |
| `SchemaScripts_ReadFromStreamingAssets_InUnity` / `MissingReader_FailsLoudly_AndVersionInspectionNeverNeedsAScript` | The StreamingAssets reader resolves all 11 scripts; without it the failure names the remedy; lazy registration keeps `MaxVersion` usable |

### Consequence worth knowing: the canonical database is content-empty

The canonical store is currently a valid but empty authoring database — 0
sessions, 0 cards, 0 phases, 0 `action_instance` rows at schema v11 (1 session
type, 1 resource, 1 temperature). The read-only test therefore asserts per-item
invariants, not counts. Real content for the load→engine proof comes from the
preserved `DotNet/Game.Content.Sqlite.Tests/Fixtures/GameContent-v1.db`, copied
and migrated in-test. **Ticket 04 cannot demonstrate a real Session until WPF
authors content into the canonical database.**

## Still unproven

- **PlayMode.** Every test above is EditMode (`includePlatforms: ["Editor"]`), so
  nothing here covers a player build, IL2CPP, or stripping.
- **Non-Windows natives.** Only the Windows pair is vendored; the
  cross-platform matrix above is the outstanding work.
- **Concurrent WPF write during a Unity read.** The read transaction is proven
  to work; a live WPF-commits-while-Unity-reloads demonstration belongs to
  ticket 04, where Repeat re-reads committed content at a run boundary.
