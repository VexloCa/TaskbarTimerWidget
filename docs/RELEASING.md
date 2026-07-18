# Releasing

Tagged releases are built and published by GitHub Actions on a Windows runner. Release archives and checksums should not be created or uploaded manually.

## Before tagging

1. Update `VERSION` with the new semantic version, without a leading `v`.
2. Move relevant entries from **Unreleased** to a dated section in `CHANGELOG.md`.
3. Update the comparison links at the bottom of `CHANGELOG.md`.
4. Run the release-equivalent build locally:

   ```powershell
   .\build.ps1 -Package
   ```

5. When taskbar positioning, DPI, ownership, or Explorer recovery changed, also run:

   ```powershell
   .\build.ps1 -RunSmokeTest
   ```

6. Commit the version and changelog changes, then merge them into `master`.

## Create the release

Create and push a signed or annotated tag that exactly matches `v` followed by the contents of `VERSION`:

```powershell
git tag -a v1.0.0 -m "Taskbar Timer Widget 1.0.0"
git push origin v1.0.0
```

The release workflow validates the tag against `VERSION`, rebuilds the executable, runs the logic tests, produces a versioned ZIP and SHA-256 checksum, and creates the GitHub Release with generated notes.

## After release

- Download the ZIP and checksum from GitHub Releases and verify the hash.
- Test the packaged installer and portable executable on a clean Windows account.
- Confirm that the executable Properties dialog shows the expected file and product versions.
- Keep the release if validation succeeds; otherwise delete the release and tag, fix the problem, and publish a new patch version.

## Code signing

The automated workflow currently produces an unsigned executable. Before wider distribution, obtain a trusted Authenticode certificate, store it through an appropriate signing service or protected GitHub environment, and add signing before the checksum and archive steps. Never store a certificate or private key in the repository.
