# Release Guide for Maintainers

This document describes how to create a new release of the Jellyfin Preview Segment Plugin using the automated GitHub workflows.

## Prerequisites

- Push access to the repository
- Git configured on your local machine

## Creating a Release

### 1. Prepare the Release

Before creating a release, ensure:
- All changes for the release are merged to the `main` branch
- The plugin builds successfully (the build workflow should pass)
- You've tested the plugin functionality

### 2. Update Version Information (Optional)

If you want to update version information in the code:
- Update the version in `Jellyfin.Plugin.PreviewSegment/build.json`
- Update any other version references in documentation

### 3. Create and Push a Version Tag

The release workflow is triggered by pushing a tag that follows semantic versioning:

```bash
# Make sure you're on the main branch and up to date
git checkout main
git pull origin main

# Create a new tag (replace X.Y.Z with your version)
git tag v1.0.0

# Push the tag to GitHub
git push origin v1.0.0
```

### 4. Monitor the Release Workflow

1. Go to the [Actions tab](https://github.com/brunobeeee/jellyfin-add-preview-segment/actions) on GitHub
2. You should see the "Release Plugin" workflow running
3. Wait for the workflow to complete (usually takes 1-2 minutes)

### 5. Verify the Release

Once the workflow completes:
1. Go to the [Releases page](https://github.com/brunobeeee/jellyfin-add-preview-segment/releases)
2. You should see a new release with:
   - Release title: version number
   - Pre-packaged ZIP file: `jellyfin-plugin-previewsegment_X.Y.Z.zip`
   - Standalone DLL: `Jellyfin.Plugin.PreviewSegment.dll`
   - SHA256 checksum in the release notes
   - Installation instructions

### 6. Edit Release Notes (Optional)

You can edit the release notes to add:
- Detailed changelog
- Breaking changes
- Known issues
- Special upgrade instructions

### 7. Update build.json (For Plugin Repository)

If you want to publish this plugin to a Jellyfin plugin repository:

1. After the release is created, copy the SHA256 checksum from the release notes
2. Update `Jellyfin.Plugin.PreviewSegment/build.json`:
   - Update the `version` field
   - Update the `sourceUrl` with the correct version number
   - Update the `checksum` with the SHA256 from the release
   - Update the `timestamp` to the release date
3. Commit and push the changes

Example:
```json
{
  "version": "1.0.0.0",
  "changelog": "Initial release with automated builds",
  "targetAbi": "10.9.0.0",
  "sourceUrl": "https://github.com/brunobeeee/jellyfin-add-preview-segment/releases/download/v1.0.0/jellyfin-plugin-previewsegment_1.0.0.zip",
  "checksum": "abc123...",
  "timestamp": "2026-01-01T12:00:00Z"
}
```

**Note**: The git tag version (e.g., `v1.0.0`) uses 3-part semantic versioning, while the plugin version in build.json uses 4-part versioning (e.g., `1.0.0.0`). The release workflow uses the 3-part version from the tag for file names.

## Version Numbering

Follow [Semantic Versioning](https://semver.org/):
- **MAJOR** (X.0.0): Incompatible API changes or major features
- **MINOR** (1.X.0): New functionality in a backward-compatible manner
- **PATCH** (1.0.X): Backward-compatible bug fixes

### Version Format Notes

- **Git tags**: Use 3-part semantic versioning (e.g., `v1.0.0`)
- **Plugin version** (in build.json): Uses 4-part versioning (e.g., `1.0.0.0`)
- **Release files**: Named using the 3-part version from the git tag

For most releases, you can use `.0` as the fourth component in build.json.

## Troubleshooting

### Workflow Fails to Start
- Ensure the tag follows the pattern `v*.*.*` (e.g., `v1.0.0`, `v2.1.3`)
- Check that you pushed the tag: `git push origin <tag-name>`

### Build Fails
- Check the workflow logs in the Actions tab
- Common issues:
  - Missing dependencies
  - Compilation errors
  - .NET SDK version mismatch

### Release Not Created
- Check workflow permissions (requires `contents: write`)
- Verify `GITHUB_TOKEN` is available
- Check workflow logs for errors

### Wrong Files in Release
- Verify the build output path is correct
- Check the workflow file paths in the `files:` section

## Manual Release (Fallback)

If the automated workflow fails, you can create a release manually:

1. Build the plugin locally:
   ```bash
   cd Jellyfin.Plugin.PreviewSegment
   dotnet build -c Release
   ```

2. Create the directory structure:
   ```bash
   mkdir -p release/Jellyfin.Plugin.PreviewSegment
   cp bin/Release/net8.0/Jellyfin.Plugin.PreviewSegment.dll release/Jellyfin.Plugin.PreviewSegment/
   ```

3. Create a ZIP file:
   ```bash
   cd release
   zip -r jellyfin-plugin-previewsegment_X.Y.Z.zip Jellyfin.Plugin.PreviewSegment/
   ```

4. Calculate checksum:
   ```bash
   sha256sum jellyfin-plugin-previewsegment_X.Y.Z.zip
   ```

5. Create the release on GitHub manually and upload the files

## Continuous Integration

The build workflow runs automatically on:
- Every push to `main` branch
- Every pull request to `main` branch

This ensures that the code always builds successfully before releases are created.

## Support

If you encounter issues with the release process:
1. Check the [GitHub Actions documentation](https://docs.github.com/en/actions)
2. Review the workflow files in `.github/workflows/`
3. Check the Actions logs for detailed error messages
