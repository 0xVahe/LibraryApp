## Title format
`type(scope): summary (REQ-ID)`  e.g. `feat(staff): add CanBorrowAsync (MEM-05)`

## What and why
<!-- 1-3 sentences -->

## Requirement IDs
<!-- e.g. MEM-05, BR-MEM-2 -->

## Type of change
- [ ] feature  
- [ ] fix  
- [ ] hotfix  
- [ ] test  
- [ ] docs
- [ ] refactor
- [ ] ci  
- [ ] revert

## Touches (tick all that apply, each needs its own approval)
- [ ] My own module only
- [ ] Another group's module: ______
- [ ] Domain project (Integration Owner + entity owner)
- [ ] Contract in `Application/Abstractions` or event fields (provider + all consumers; Tutor after freeze)
- [ ] Shared files (`Program.cs`, CI, docker-compose)
- [ ] Entity or mapping change, label `needs-migration` (no migration file from me)

## Depends on
<!-- `Depends on #N`, or "none" -->

## Definition of Done
- [ ] Implements the requirement and its acceptance criteria
- [ ] Business rules covered by unit tests, all tests pass
- [ ] Endpoint visible and documented in Swagger
- [ ] Validation and error handling return correct status codes (ProblemDetails)
- [ ] No secrets, no real personal data, no sensitive data in logs
- [ ] Branch is up to date with the latest `main`, review comments resolved
- [ ] Checks are green (build, tests, coverage, secret scan, title)

## How to test
<!-- Swagger call or test name, expected result -->

## Reviewers
- Author is not the approver
- QA Champion checks the Definition of Done before merge
