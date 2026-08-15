# Release Guide for Maintainers

Releasing is fully automated. Pushing a `vX.Y.Z` tag on `main` builds the plugin, publishes a GitHub
Release, and updates the repository manifest that Jellyfin subscribes to — no manual manifest editing.

## How it works

The [`Release Plugin`](.github/workflows/release.yml) workflow runs on any pushed `v*.*.*` tag and:

1. Builds the plugin with [jprm](https://github.com/oddstr13/jellyfin-plugin-repository-manager),
   packaging a `preview-segment_X.Y.Z.0.zip` that contains the DLL **and** an embedded `meta.json`
   (generated from [`Jellyfin.Plugin.PreviewSegment/build.yaml`](Jellyfin.Plugin.PreviewSegment/build.yaml)).
2. Appends a version entry to [`manifest.json`](manifest.json) with the release download URL as
   `sourceUrl` and the zip's **MD5** as `checksum` (Jellyfin verifies plugin zips by MD5).
3. Publishes a GitHub Release with the zip attached.
4. Commits the updated `manifest.json` back to `main`, so the repository URL immediately serves the
   new version.

The repository URL users add in Jellyfin (Dashboard → Plugins → Repositories) is:

```
https://raw.githubusercontent.com/brunobeeee/jellyfin-add-preview-segment/main/manifest.json
```

## Creating a release

1. Merge everything for the release into `main` and make sure the build workflow is green.
2. (Optional) Update the `changelog` in
   [`Jellyfin.Plugin.PreviewSegment/build.yaml`](Jellyfin.Plugin.PreviewSegment/build.yaml). The
   `version` field there is a placeholder — the tag drives the actual release version.
3. From `main`, tag and push:

   ```bash
   git checkout main && git pull
   git tag v1.1.0
   git push origin v1.1.0
   ```

4. Watch the [Actions tab](https://github.com/brunobeeee/jellyfin-add-preview-segment/actions). When
   the workflow finishes, verify:
   - a new [Release](https://github.com/brunobeeee/jellyfin-add-preview-segment/releases) with
     `preview-segment_1.1.0.0.zip` attached, and
   - a new commit on `main` updating `manifest.json` (the newest `versions[]` entry has a non-empty
     `checksum` and a `sourceUrl` pointing at the release asset).

> **Always tag on `main`'s HEAD.** The workflow checks out `main` (not the tag) so it can commit the
> manifest back; tag a different commit and the built code won't match the tag.

## Versioning

- **Git tags** use 3-part SemVer: `vX.Y.Z`.
- The plugin/manifest version is the 4-part `X.Y.Z.0` (Jellyfin convention), derived automatically
  from the tag.

Follow [SemVer](https://semver.org/): MAJOR for breaking changes, MINOR for features, PATCH for fixes.

## One-time setup: allow the manifest push to `main`

The workflow pushes the manifest commit to `main` using the default `GITHUB_TOKEN`. If `main` is a
**protected branch**, the push will be rejected. Either:

- add an exception so `github-actions[bot]` can push to `main`, or
- push the manifest from a Personal Access Token / deploy key with the necessary permission.

## Troubleshooting

- **Workflow didn't start** — the tag must match `v*.*.*` and be pushed (`git push origin <tag>`).
- **Manifest not updated on `main`** — almost always branch protection blocking the push (see above);
  check the "Commit updated manifest to main" step logs.
- **Jellyfin refuses to install / "checksum mismatch"** — the manifest `checksum` must be the zip's
  MD5. jprm handles this; only relevant if a manifest entry was hand-edited.
- **Build fails** — check the jprm/build logs; usually a compile error or an SDK mismatch (the plugin
  targets `net9.0`).

## Manual fallback

If the workflow is unavailable, reproduce it locally with jprm:

```bash
pip install jprm
mkdir -p ./artifacts
zipfile=$(jprm plugin build ./Jellyfin.Plugin.PreviewSegment --output=./artifacts --version=1.1.0.0)
jprm repo add \
  --plugin-url="https://github.com/brunobeeee/jellyfin-add-preview-segment/releases/download/v1.1.0/$(basename "$zipfile")" \
  ./manifest.json "$zipfile"
```

> Use `--plugin-url` (the full flat zip URL), **not** `--url` — the latter inserts a per-plugin
> subdirectory that GitHub release assets don't have, producing a 404 `sourceUrl`.

Then upload `$zipfile` to a GitHub Release tagged `v1.1.0` and commit the updated `manifest.json`.

## Continuous integration

The [build workflow](.github/workflows/build.yml) runs on every push and pull request to `main`,
ensuring the plugin always compiles before a release is cut.
