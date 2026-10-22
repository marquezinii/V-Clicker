# Security

V-Clicker is a single `.exe` with no installer, no telemetry and no network access of its own (the optional AI Macro
Builder talks only to the Anthropic API, with the user's key, when the user presses *Generate*).

## Reporting a vulnerability

Please report security issues privately through
[GitHub's private vulnerability reporting](https://github.com/marquezinii/V-Clicker/security/advisories/new) rather
than in a public issue. You will get an answer within a week; fixes ship as a new release with a changelog entry.

## Supported versions

Only the [latest release](https://github.com/marquezinii/V-Clicker/releases/latest) receives fixes.

## Verifying a download

Every release ships with SHA-256 files, a Sigstore signature bundle and a GitHub build-provenance attestation, and the
release notes list the commands that check them. CodeQL, the OpenSSF Scorecard and Dependabot run on the repository
(see [docs/ci-cd.md](docs/ci-cd.md)).
