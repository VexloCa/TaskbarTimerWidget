# GitHub Repository Setup

The files in the repository provide the product page, contribution flow, CI, and tagged release automation. Complete these one-time repository settings after pushing the source.

## About section

- **Description:** A lightweight, always-visible countdown timer for the Windows taskbar.
- **Topics:** `windows`, `windows-11`, `timer`, `countdown`, `taskbar`, `winforms`, `csharp`, `productivity`
- Enable the **Releases** and **Packages** links as appropriate. Packages are not currently used.
- Upload a 1280 × 640 social preview image under **Settings → General → Social preview**.

## Features and security

- Enable **Issues** so the issue forms in `.github/ISSUE_TEMPLATE` are available.
- Enable **Private vulnerability reporting** under **Settings → Security** so `SECURITY.md` has a confidential reporting path.
- Optionally enable **Discussions** for usage questions and ideas that are not actionable issues.
- Enable secret scanning and push protection where available.

## Branch protection

Protect the default branch (`master` today, or `main` if renamed):

- require a pull request before merging;
- require the `build-and-test` status check;
- require branches to be up to date before merging;
- dismiss stale approvals when new commits are pushed; and
- block force pushes and deletion.

If the default branch is renamed, both `master` and `main` are already accepted by the CI workflow.

## Actions

- Keep the default `GITHUB_TOKEN` permission read-only. The release workflow requests `contents: write` only for its release job.
- Allow actions created by GitHub. The workflows currently use only `actions/checkout` and `actions/upload-artifact`, plus the GitHub CLI preinstalled on hosted runners.
- Review workflow dependency versions periodically and pin them to full commit SHAs if stricter supply-chain controls are required.

## First release

Follow [`RELEASING.md`](RELEASING.md) after the default branch passes CI. Do not upload the executable from the repository root; publish only the ZIP and checksum produced by the tagged release workflow.
