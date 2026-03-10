---
description: Git workflow conventions for commits, branches, and merges
globs:
---

# Git Conventions

## Branching

- Work on feature branches, NOT directly on main/master
- Branch naming: `feature/<descriptive-name>`
- Commit changes with conventional commit messages
- Only merge to main when the user approves
- After merge approval: merge with `--no-ff`, push, then delete the feature branch
- Keep branches focused on a single task/feature

## Conventional Commits

Use prefixes: `feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`, `style:`

Example: `feat: add settings persistence via JSON file`
