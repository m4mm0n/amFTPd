# amFTPd Continuation Context (2026-05-02)

Status:
- Main branch work is a broad in-progress refactor with many feature additions for scene parity.
- I added integration coverage for full PRE workflow in `amFTPd.Tests/PreTests.cs` (`FullPreWorkflow_QueuesApproveAndDeniesPresAcrossPendingAndLivePaths`).
- Existing major gaps list (from PLAN/NEWPLAN/gap report):
  - Multi-client STOR race on same release/file robustness
  - REST/RESUME interruption/corruption matrix
  - FXP/active/passive/TLS matrix hardening
  - Migration fixture parity for ioFTPD + glFTPD
  - IRC announce smoke test against a real IRC server
  - Long-running soak/restart persistence for login/upload/download/PRE/NUKE
- A `Treasure Cove` synthetic site scenario is already in place via `FtpTestFixture`.

High-priority next actions:
1) Add `SceneReadiness/Regression` tests for:
   - PRE full workflow + denial/audit (newly added, verify passes in full suite)
   - Concurrent same-file/section STOR, multi-uploader race stats + zipscript consistency
   - REST resume bad partial / missing partial edge cases
2) Add focused migration tests with real fixture data for both ioFTPD/glFTPD formats.
3) Run and stabilize targeted suites:
   - `dotnet test amFTPd.Tests`
   - `dotnet format --verify-no-changes` if project style gate is part of your pipeline.

Run order suggestion:
- Keep adding tests first for each gap, then patch behavior once the failing assertion identifies the gap.
- Treat `SceneReplacementGapReport.md` + `PLAN.md` as acceptance criteria baseline.