# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org).

> Status: the application to the SignPath Foundation is pending. Until it is approved, Windows releases are
> **not signed**; this page describes the rules that signed releases will follow.

## What is signed

Only the Windows binaries built from this repository: the DoveLoFanno server (`DoveLoFanno.Server.exe`,
`DoveLoFanno.Core.dll`, `DoveLoFanno.Server.dll`) and, later, the DoveLoFanno Windows app and its installer.
Third-party binaries (the .NET runtime, SQLite, ffmpeg) are never signed with this certificate: ffmpeg is not shipped
at all and is installed separately by the user.

Every signed file carries the same metadata: product name `DoveLoFanno` and the version of the release
(set once in [Directory.Build.props](Directory.Build.props) and from the release tag).

## How a release is signed

1. A version tag `vX.Y.Z` is pushed to this repository.
2. The [release workflow](.github/workflows/release.yml) builds the binaries from that tag on GitHub-hosted runners
   (no local builds are ever signed).
3. The unsigned build artifact is submitted to SignPath, which verifies that it comes from that workflow run.
4. An approver reviews and **manually approves** the signing request.
5. The signed files are published on [dovelofanno.it](https://dovelofanno.it) (download.dovelofanno.it); their
   SHA-256 checksums are attached to the matching [Release](https://github.com/antoscool73/dovelofanno/releases)
   on GitHub, next to the source code.

Build scripts and CI configuration are part of the source code and follow the same review rules.

## Team roles

| Role | Members |
| --- | --- |
| Committers and reviewers | [antoscool73](https://github.com/antoscool73) |
| Approvers | [antoscool73](https://github.com/antoscool73) |

All team members use multi-factor authentication for GitHub and SignPath. Changes from people outside the team
(pull requests) are reviewed by a team member before they are merged.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user
or the person installing or operating it.

DoveLoFanno has no telemetry, no accounts and no cloud service. It connects only to the services that the user
configures:

- the M3U playlists, stream URLs and XMLTV guide URLs that the user adds;
- [TMDB](https://www.themoviedb.org) (`api.themoviedb.org`, `image.tmdb.org`), only after the user enters their own
  TMDB API key; title searches are sent to TMDB with that key;
- the IMDb ratings dataset (`datasets.imdbws.com`), only if the user turns on IMDb ratings;
- YouTube, only when the user opens a trailer link.

All data (profiles, lists, recordings, library) stays in the server's data folder on the user's own hardware.
