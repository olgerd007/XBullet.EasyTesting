## Summary

Describe the behavior being changed and why.

## Validation

- [ ] `dotnet format XBullet.EasyTesting.sln --verify-no-changes --no-restore`
- [ ] `dotnet test XBullet.EasyTesting.sln --configuration Release --no-build`
- [ ] `./eng/verify-documentation.ps1 -NoBuild -WarningsAsErrors`
- [ ] Public behavior changes update the appropriate guide, package README, XML comments, API
      package page, and `docs/contributing/documentation-coverage.md`; or the summary explains why documentation
      is not required
- [ ] Breaking changes include migration guidance and a changelog entry
- [ ] Snapshot changes were reviewed and only verified files are committed
