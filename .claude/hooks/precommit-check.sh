#!/bin/bash
# precommit-check.sh — Run tests before git commit/push commands

INPUT=$(cat)
COMMAND=$(echo "$INPUT" | jq -r '.tool_input.command // empty')

# Only check if this looks like a git commit or push
if [[ "$COMMAND" =~ git[[:space:]]+commit|git[[:space:]]+push ]]; then
  cd "$(git rev-parse --show-toplevel)" || exit 0

  if ! dotnet test AudioLeash.sln -q 2>&1; then
    echo "Tests failed. Fix failures before committing." >&2
    exit 2
  fi
fi

exit 0
