# /work-issue — branch, PR and hand-off mechanics for a GitHub issue

Take the GitHub issue in $ARGUMENTS (issue number) from "not started" to "draft PR open".

**This command owns the git mechanics only.** The thinking — how to solve it, in what order, test-first or not — belongs to the skills being trialled: `/matt:implement` for work that already has a spec or tickets, `/matt:grilling` when the approach is not settled, `/matt:tdd` at the seams, `/matt:diagnosing-bugs` when the issue is a defect. This command exists because none of them touch branches, draft PRs or `Closes #N`, and because the rules below were learned the hard way in this repo.

## 1. Read the issue

```
gh issue view $ARGUMENTS --json number,title,labels,body --comments
```

If the issue carries an agent brief from `/matt:triage`, that brief is the spec — do not re-derive it.

## 2. Branch from a fresh master

A stale local master is the commonest cause of fixes that target code that no longer exists. On #1297 local master was 10+ commits behind and the fix landed against a UI a squash-merged PR had already replaced the day before.

```
git fetch origin master
git checkout master
git pull --ff-only           # refuses if local master diverged; never creates a merge commit
git checkout -b feature/$ARGUMENTS-<short-slug> master
```

`<short-slug>` is a 2–4 word kebab-case summary, e.g. `feature/1076-alert-schedule-unique-name`.

Before writing code, confirm that the UI patterns, file paths or signatures the issue names actually exist at this base. A mismatch means the base is stale — re-fetch and re-branch.

## 3. Do the work

Hand over to the trialled skills. Keep the change inside the issue's scope; build to verify compilation.

## 4. Commit

```
git add <changed files>
git commit -m "Short description of the change

Longer explanation if needed.

Closes #$ARGUMENTS

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

`Closes #N` goes in before the PR is merged — added afterwards it does not close the issue.

## 5. Review your own diff

Each push reviews what it adds, after it is committed (a range sees committed work only).

First push of the branch — the whole branch:

```
git fetch origin master
/code-review medium origin/master...HEAD     # or: /matt:code-review origin/master
```

Fetch first: the range uses your local copy of `origin/master`, and a stale one puts the merge base too far back, so the diff picks up commits that are already in master. The range must be named: before the first push there is no upstream branch to default to.

Later pushes — only the new commits:

```
/code-review medium @{upstream}..HEAD
```

Already-reviewed commits are not reviewed again, so findings you declined do not come back.

Adjudicate the findings the way you would the PR bot's — fix defects, decline "do what nobody asked" — commit the fixes, then rebuild and re-run the narrowest tests for what you fixed. Commits that only fix a review finding are re-verified, not re-reviewed; anything beyond the finding is new work and gets the later-push review. The bot is the second reader; a round of it costs an API call and a full rebuild/verify cycle.

## 6. Push

```
git push -u origin feature/$ARGUMENTS-<short-slug>
```

## 7. Open a draft PR

```
gh pr create --draft --base master --title "Short description" --body "..."
```

Then stop. Never merge to `master` — review and merge are the human's call, green checks are not permission.
