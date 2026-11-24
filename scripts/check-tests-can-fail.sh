#!/usr/bin/env bash
# Proves the tests can fail: breaks one rule of the code at a time, runs the tests, and expects them to fail.
# Each break must compile (a build error would "fail" for the wrong reason, so it counts as a gate failure).
# The original file is restored after each break, even if the script is interrupted.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
backup="$root/.dev/mutation-backup"
mkdir -p "$root/.dev"

# file @ text to find (exactly once) @ text to put instead @ what the break means
breaks=(
  "src/Deskbook.Core/BookCalculator.cs@t.TradeDate > previous.Date@t.TradeDate >= previous.Date@a trade done on the previous day counts as new"
  "src/Deskbook.Core/BookCalculator.cs@openingAmount * (rate - previousRate!.Value)@openingAmount * (previousRate!.Value - rate)@revaluation has the wrong sign"
  "src/Deskbook.Core/RateCalendar.cs@if (!IsFetched(day)) unfetched.Add(day);@if (IsFetched(day) && !IsFetched(day)) unfetched.Add(day);@a hole in the fetched days is ignored"
  "src/Deskbook.Core/BookCalculator.cs@missing.Add(\$\"no {currency} rate published for {date:yyyy-MM-dd}\");@rate = 0m;@a currency with no rate is silently left out of the book"
  "src/Deskbook.Infrastructure/Postgres/PostgresStore.cs@else if (old == rate)@else if (old >= 0)@a revised rate is silently ignored"
  "src/Deskbook.Infrastructure/Valet/ValetClient.cs@if (reachesTo || now - cachedAt < MinimumRefetchInterval)@if (cachedDays.Count >= 0 || reachesTo)@a cached answer from before publication is reused all day"
)

# A failure only counts as a catch if the same tests pass with nothing broken, before and after: otherwise a
# database that is down would make every break look caught.
green() {
  local output
  output="$(scripts/dev.sh dotnet test 2>&1 || true)"
  if grep -q "Failed!" <<< "$output" || ! grep -q "Passed!" <<< "$output"; then
    echo "GATE ERROR: the tests do not pass $1 any break (is the database up? scripts/dev.sh db-up)" >&2
    exit 1
  fi
}

restore() { if [ -f "$backup" ] && [ -n "${current:-}" ]; then cp "$backup" "$current"; fi; }
trap restore EXIT

green "before"

caught=0
for entry in "${breaks[@]}"; do
  IFS='@' read -r current from to meaning <<< "$entry"
  content="$(cat "$current")"
  count="$(grep -cF -- "$from" "$current" || true)"
  if [ "$count" != "1" ]; then echo "GATE ERROR: '$from' found $count time(s) in $current" >&2; exit 1; fi
  cp "$current" "$backup"
  printf '%s\n' "${content/"$from"/"$to"}" > "$current"

  output="$(scripts/dev.sh dotnet test 2>&1 || true)"
  cp "$backup" "$current"

  if grep -q "error CS" <<< "$output"; then
    echo "GATE ERROR: the break '$meaning' does not compile" >&2; exit 1
  elif grep -q "Failed!" <<< "$output"; then
    caught=$((caught + 1)); echo "caught:  $meaning"
  else
    echo "MISSED:  $meaning (all tests passed with the rule broken)"
  fi
done
current=""
green "after"

echo "$caught of ${#breaks[@]} breaks caught by the tests."
[ "$caught" -eq "${#breaks[@]}" ]
