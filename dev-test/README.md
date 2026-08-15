# Local Dev-Test Harness

A throwaway, scriptable Jellyfin instance (Docker) to build, install, and **end-to-end test**
the Preview Segment plugin against the **latest** Jellyfin release. Everything except the
`scripts/` and `docker-compose.yml` is runtime state and is gitignored.

## Requirements
- Docker (running), `sqlite3`, `ffmpeg`, `python3`, `curl`. **No host `dotnet` needed** — the
  plugin is built inside a `dotnet/sdk:8.0` container.

## Usage
Run the numbered scripts in order from the repo root:

```bash
./dev-test/scripts/01-build-plugin.sh     # build DLL in a dotnet SDK container
./dev-test/scripts/02-make-media.sh       # ffmpeg -> media/Test Show/Season 01/Test Show S01E01.mkv
./dev-test/scripts/03-setup-jellyfin.sh   # start JF, run startup wizard, create TV library, scan
./dev-test/scripts/04-install-plugin.sh   # install DLL + meta.json, restart, report plugin status
./dev-test/scripts/05-seed-intro.sh       # inspect MediaSegments schema, seed an Intro (Type=5)
./dev-test/scripts/06-run-and-verify.sh   # set config, run the task, verify Preview via DB + API
```

- Web UI: <http://localhost:8096>  (login `admin` / `previewsegment`)
- Shared state (token, ids) is written to `dev-test/.state/`.
- Live logs: `docker compose -f dev-test/docker-compose.yml logs -f`
- **Teardown:** `docker compose -f dev-test/docker-compose.yml down` (keep data) or
  `... down -v` + `rm -rf dev-test/config dev-test/cache dev-test/.state` for a clean slate.

### Useful env overrides for `05-seed-intro.sh`
- `ITEMID_STYLE=upper-hyphen|upper-nohyphen|lower-hyphen|lower-nohyphen` (JF 10.11 uses `upper-hyphen`)
- `TYPE_VALUE=5` (Intro; stored as INTEGER), `INTRO_START_S`, `INTRO_END_S`

---

## Findings (2026-08-15, against Jellyfin **10.11.11**)

The instance auto-installed the **latest** Jellyfin: **10.11.11**. 10.11 is the "great migration"
release — library items (`BaseItems`) now live in the EF-Core `jellyfin.db` alongside `MediaSegments`.

### 1. The dashboard error is a harmless web-UI symptom
`"Beim Abrufen der Plugin-Details aus dessen Software-Depot ist ein Fehler aufgetreten."`
- The plugin **loads fine** on 10.11.11 (`Status: Active`), with **or without** `meta.json`.
- The plugin **details page** calls `GET /Packages/{name}?assemblyGuid={guid}`, which returns
  **404** because this plugin's GUID is not published in any configured plugin repository. That 404
  is what produces the toast. It does **not** prevent the plugin from loading or the config page
  (`/Plugins/{guid}/Configuration` → `200`) from working.

### 2. The real bug: segments are written but never surfaced
Running the task on a seeded Intro:
- The task **succeeds at the DB level** — it reads the Intro (SQLite coerces `INTEGER` `Type`→`"5"`
  and `TEXT` `Id`→`0`, so the plugin's `GetInt32`/`GetString` mismatches don't crash) and
  **inserts a Preview row** (`Type=2`, `0→5s`). Both rows are in `MediaSegments`.
- **But `GET /MediaSegments/{itemId}` returns `[]`** — nothing reaches the player.
- Root cause: Jellyfin filters segment results to **currently-registered `ISegmentProvider`s**.
  The plugin writes rows under a **spoofed provider id** `b0338b450421c081992860f1d02f261f`
  (Intro Skipper), which is **not installed/registered** here → every row is filtered out. Even the
  Intro seeded with that provider id is invisible to the API.
- Storage-format facts confirmed for 10.11: GUIDs are stored **UPPERCASE with hyphens**
  (`400D4F07-64B8-…`); the API returns lowercase-no-hyphens. `MediaSegments.Type` is **INTEGER**.

### Fix (implemented and verified with this harness)
The direct `INSERT`s were replaced with a proper **media segment provider**
(`Providers/PreviewSegmentProvider.cs`), registered via `PluginServiceRegistrator.cs`. Jellyfin's
built-in **Extract Media Segments** task now invokes the provider, which reads the episode's Intro
via `IMediaSegmentManager.GetSegmentsAsync(...)` and returns a `Preview` segment. Jellyfin stores it
under the plugin's **own registered provider id** (`MD5("preview segment")`), so it is surfaced by
`GET /MediaSegments/{itemId}`.

Verified end-to-end on Jellyfin 10.11.11: after seeding an Intro and running **Extract Media
Segments**, the `Preview` row is created under our provider id **and returned by the API** (the old
approach wrote the row but it was filtered out). The plugin now targets `net9.0` / `targetAbi
10.11.0.0` (Jellyfin 10.11 runs on .NET 9); build with the `dotnet/sdk:9.0` container. There is no
longer any plugin config or custom scheduled task — enablement is per-library via Jellyfin's *Media
Segment Providers* settings.
