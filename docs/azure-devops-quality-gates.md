# Azure DevOps Quality Gates

This repo is intended to use [`azure-pipelines.yml`](../azure-pipelines.yml) as the required build-validation pipeline for protected branches.

## Pipeline behavior

The pipeline runs automatically for:

- pushes to `main` and `master`
- pull requests targeting `main` and `master`

PR runs use `autoCancel: true` so superseded validations are canceled when a newer commit is pushed to the same PR.

The validation job enforces these gates in order:

1. `dotnet restore` with NuGet auditing enabled by the Azure pipeline
2. `dotnet format KalshiIntegrationEventPublisher.sln --verify-no-changes`
3. `dotnet build` in `Release` with analyzers and code-style warnings treated as errors
4. `dotnet test` for the full .NET solution with TRX publishing and XPlat code coverage collection
5. `node --test` in `node-gateway`
6. Cobertura coverage publishing from the generated test-results output

## Analyzer and dependency enforcement

Repo-wide build settings in [`Directory.Build.props`](../Directory.Build.props) enforce:

- .NET analyzers enabled
- `latest-recommended` analysis level
- code-style enforcement during build
- warnings treated as errors
- local/default restores keep `NuGetAudit` off so cached-package workflows do not fail when external feeds are unavailable
- restore tolerance for failed package feeds when dependencies are already present in cache
- elevation of high and critical NuGet vulnerability warnings to errors (`NU1903`, `NU1904`) when audit is enabled

The Azure DevOps restore step turns on NuGet auditing for direct and transitive packages with:

- `NuGetAudit=true`
- `NuGetAuditMode=all`
- `NuGetAuditLevel=high`

The Node gateway currently has no external npm dependencies, so dependency scanning for this story is limited to the NuGet restore graph.

## Recommended branch-policy setup

Configure Azure DevOps branch policies for `main` and `master` so the YAML pipeline is actually required before merge.

Recommended minimum policy set:

1. Require pull requests for changes into the protected branch.
2. Add this pipeline as a required build validation and enable automatic queueing.
3. Expire the build validation when the target branch updates so stale PR results cannot be reused.
4. Require at least one reviewer and require comment resolution before completion.
5. Block direct pushes for contributors who should merge through PRs only.

In Azure DevOps, configure this under `Repos -> Branches -> Branch policies`, then select the pipeline created from `azure-pipelines.yml` as the build-validation policy.
