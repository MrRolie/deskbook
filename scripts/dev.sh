#!/usr/bin/env bash
# Development helper. Runs the pinned official images with Docker; nothing is installed on the machine.
#   scripts/dev.sh db-up          start PostgreSQL on a private Docker network (no published port)
#   scripts/dev.sh dotnet <args>  run the .NET SDK against this checkout, as the calling user
#   scripts/dev.sh psql [args]    open psql inside the database container
#   scripts/dev.sh web ADDR:PORT  serve the book page on that address only (the database stays unpublished)
# State (database password, NuGet cache) lives in .dev/, which git ignores. The Docker network, container and volume
# are named from DESKBOOK_DEV_PREFIX (default "deskbook"), so a second checkout can run beside the first.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
dev="$root/.dev"
sdk_image="mcr.microsoft.com/dotnet/sdk@sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d"
pg_image="postgres@sha256:f5cbc081c25690710f3fc6f4c61a7565382bae01ca0d0f2028a53d7926b85a20"
prefix="${DESKBOOK_DEV_PREFIX:-deskbook}"
network="$prefix-net"
pg_name="$prefix-pg"

mkdir -p "$dev/home" "$dev/nuget"
if [ ! -f "$dev/pg.env" ]; then
  (umask 077; printf 'POSTGRES_PASSWORD=%s\n' "$(head -c 18 /dev/urandom | base64 | tr -d '/+=')" > "$dev/pg.env")
fi
password="$(sed -n 's/^POSTGRES_PASSWORD=//p' "$dev/pg.env")"

case "${1:-}" in
  db-up)
    docker network inspect "$network" >/dev/null 2>&1 || docker network create "$network" >/dev/null
    if ! docker container inspect "$pg_name" >/dev/null 2>&1; then
      docker run -d --name "$pg_name" --network "$network" --env-file "$dev/pg.env" \
        -e POSTGRES_USER=deskbook -e POSTGRES_DB=deskbook \
        -v "$prefix-pgdata:/var/lib/postgresql" "$pg_image" >/dev/null
    else
      docker start "$pg_name" >/dev/null
    fi
    for _ in $(seq 1 30); do
      if docker exec "$pg_name" pg_isready -U deskbook -q; then
        # A second database for the integration tests, so they never touch the book's data.
        if [ "$(docker exec "$pg_name" psql -U deskbook -d deskbook -tAc "SELECT 1 FROM pg_database WHERE datname = 'deskbook_test'")" != "1" ]; then
          docker exec "$pg_name" psql -U deskbook -d deskbook -qc "CREATE DATABASE deskbook_test" >/dev/null
        fi
        echo "database ready"; exit 0
      fi
      sleep 1
    done
    echo "database did not become ready" >&2; exit 1 ;;
  dotnet)
    shift
    tty_flag=""; [ -t 0 ] && tty_flag="-t"
    exec docker run --rm -i $tty_flag --user "$(id -u):$(id -g)" --network "$network" \
      -e HOME=/dev-home -e DOTNET_CLI_HOME=/dev-home -e NUGET_PACKAGES=/nuget \
      -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 -e DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
      -e DESKBOOK_DB="Host=$pg_name;Username=deskbook;Password=$password;Database=deskbook" \
      -e DESKBOOK_TEST_DB="Host=$pg_name;Username=deskbook;Password=$password;Database=deskbook_test" \
      -e DESKBOOK_CACHE=/src/data/cache \
      -v "$dev/home:/dev-home" -v "$dev/nuget:/nuget" -v "$root:/src" -w /src \
      "$sdk_image" dotnet "$@" ;;
  web)
    bind="${2:?give the address and port to publish on, for example 100.64.0.1:8820}"
    exec docker run --rm -i --user "$(id -u):$(id -g)" --network "$network" -p "$bind:8080" \
      -e HOME=/dev-home -e DOTNET_CLI_HOME=/dev-home -e NUGET_PACKAGES=/nuget \
      -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 -e DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
      -e DESKBOOK_DB="Host=$pg_name;Username=deskbook;Password=$password;Database=deskbook" \
      -v "$dev/home:/dev-home" -v "$dev/nuget:/nuget" -v "$root:/src" -w /src \
      "$sdk_image" dotnet run --project src/Deskbook.Web -- --urls http://0.0.0.0:8080 ;;
  psql)
    shift
    exec docker exec -it "$pg_name" psql -U deskbook -d deskbook "$@" ;;
  *)
    sed -n '2,7p' "$0"; exit 2 ;;
esac
