# amFTPd 0.8.0.0 Release Checklist

## Required Gates

- [x] Branch exists: `codex/amftpd-0.8-polish-surpass`
- [x] Solution targets `net10.0`
- [x] `dotnet build amFTPd.sln -c Release -warnaserror`
- [x] `dotnet .\amFTPd\bin\Release\net10.0\amFTPd.dll amftpd.json --validate`
- [x] QuickLog is the default and only daemon logger
- [x] Runtime logging commands prove `EVERYTHING`, `SOMETHING`, and `QUIET` modes
- [x] Windows Service mode is removed from code, project references, deployment scripts, and supported docs
- [x] `dotnet format --verify-no-changes --verbosity minimal`
- [x] `dotnet test amFTPd.Tests\amFTPd.Tests.csproj -c Release --no-build`
- [x] Published `Ready2Release\amFTPd\amFTPd.exe` validates the release config
- [x] Published executable startup smoke accepts a control connection
- [x] Migration check passes representative ioFTPD fixture
- [x] Migration check passes representative glFTPd fixture
- [ ] Docker image starts and passes health check
- [ ] systemd unit dry-run or Linux smoke passes where available

## Release Rule

Only mark this release ready when every checked item has fresh command output or captured evidence from this branch.
