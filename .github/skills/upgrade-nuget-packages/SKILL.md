---
name: upgrade-nuget-packages
description: Upgrade all NuGet packages in this repository to their latest stable versions, validate the changes, and open a pull request. Use when asked to update or upgrade NuGet dependencies.
---

# Upgrade NuGet packages

Perform the complete upgrade in a new branch. Do not open a pull request unless every declared package has been checked and the upgrade, build, and tests succeed.

## Prepare

1. Check `git status --porcelain` and the current branch. If the worktree has any changes, stop and ask how to proceed; do not stash, overwrite, or include someone else's work.
2. Identify the repository's default branch with `gh repo view --json defaultBranchRef --jq .defaultBranchRef.name`. Fetch that branch from `origin`, then create and switch to a new, uniquely named branch such as `chore/upgrade-nuget-YYYYMMDD` from the fetched default branch. Never reuse or force-push an existing branch.
3. Inspect the current solution, project files, NuGet configuration, and `Directory.Packages.props` rather than relying on versions in documentation. This repository uses central package management and enables transitive pinning. Its solution is `RpcProvider.slnx`, and CI uses the .NET 10 SDK.

## Upgrade

1. Inventory all versioned NuGet declarations, including every `PackageVersion` in `Directory.Packages.props`, any project-level versions or overrides, and any lock files. Include transitive pins and central entries not directly referenced by a project; an outdated-package listing alone does not cover them.
2. For each declared package, find its latest stable version from the configured NuGet sources. For example, `dotnet package search <package-id> --exact-match --format json` lists stable versions from configured sources. Inspect errors and all relevant source results, and compare versions using NuGet version ordering, not lexical string ordering. Do not treat an unavailable source, a search error, or an empty result as an up-to-date package. Do not opt into prereleases unless explicitly requested.
3. Update each declaration to the latest stable version, including major versions and transitive pins. Do not downgrade an existing version. Keep central version management and existing reference metadata intact. If a latest version cannot be resolved, is incompatible, or would require a downgrade, stop and report the blocker rather than silently skipping it.
4. Make the smallest necessary code, test, and documentation changes for breaking API changes. Preserve existing behavior; do not change unrelated functionality or remove tests just to make the upgrade pass. Regenerate existing lock files if needed.
5. Recheck every versioned declaration against the latest versions found, including pins not shown in `dotnet list RpcProvider.slnx package --outdated`. Investigate and resolve restore warnings about package downgrades or dependency conflicts; do not suppress them to pass validation.

## Validate and publish

1. Run the repository's CI-equivalent commands from the root:

   ```sh
   dotnet restore RpcProvider.slnx
   dotnet build RpcProvider.slnx --configuration Release --no-restore
   dotnet test RpcProvider.slnx --configuration Release --no-build --verbosity normal
   ```

   Confirm the test project actually runs tests. Resolve failures caused by the upgrade and rerun the affected checks. If restore, build, tests, or package verification cannot succeed, do not create a PR; report the failure and leave the changes available for inspection.
2. If all packages were already current and no changes were needed, report that outcome without creating an empty commit or PR.
3. Review the diff and status for unrelated changes. Commit only the upgrade and its required compatibility changes, following repository and session commit conventions. Push the new branch to `origin` without force.
4. Check for a PR template under `.github` and follow it if present. Open a PR targeting the default branch using the environment's required PR creation tool, or `gh pr create` if no such tool is required. Describe the package version changes, any compatibility changes, and the validation results. If push or PR creation is unavailable or denied, report the exact blocker instead of claiming success.
