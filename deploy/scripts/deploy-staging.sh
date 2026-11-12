#!/usr/bin/env bash
set -euo pipefail

sha=${1:-}
source_dir=${2:-}
root=${3:-}
[[ "$sha" =~ ^[0-9a-f]{40}$ ]] || { echo 'invalid commit SHA' >&2; exit 64; }
[[ -d "$source_dir/src/AviSite.Web" ]] || { echo 'source checkout missing' >&2; exit 66; }
[[ "$root" == /home/hermes_990-005/.local/share/avi-site-testing ]] || { echo 'unexpected deployment root' >&2; exit 65; }

mkdir -p "$root/releases"
release="$root/releases/$sha"
if [[ ! -f "$release/AviSite.Web.dll" ]]; then
    staging=$(mktemp -d "$root/releases/.staging-$sha.XXXXXX")
    trap 'rm -rf -- "$staging"' EXIT
    /usr/bin/dotnet publish "$source_dir/src/AviSite.Web" -c Release -o "$staging" --nologo
    printf '%s\n' "$sha" > "$staging/.source-commit"
    mv -- "$staging" "$release"
    trap - EXIT
fi

current="$root/current"
old_target=''
if [[ -L "$current" ]]; then
    old_target=$(readlink -f -- "$current" || true)
elif [[ -e "$current" ]]; then
    echo 'deployment current path is not a symlink; refusing to replace it' >&2
    exit 65
fi

next_link="$root/.current-$sha-$$"
ln -s -- "$release" "$next_link"
mv -Tf -- "$next_link" "$current"

if ! /usr/bin/systemctl --user restart avi-site-testing.service; then
    echo 'service restart failed; rolling back' >&2
    if [[ -n "$old_target" ]]; then
        ln -sfn -- "$old_target" "$root/.current-rollback-$$"
        mv -Tf -- "$root/.current-rollback-$$" "$current"
        /usr/bin/systemctl --user restart avi-site-testing.service || true
    fi
    exit 1
fi

healthy=0
for _ in $(seq 1 30); do
    if /usr/bin/curl -fsS --max-time 3 http://127.0.0.1:5081/healthz | /usr/bin/grep -qx Healthy; then
        healthy=1
        break
    fi
    sleep 2
done
if [[ "$healthy" != 1 ]]; then
    echo 'health check failed; collecting service logs and rolling back' >&2
    /usr/bin/journalctl --user -u avi-site-testing.service -n 50 --no-pager >&2 || true
    if [[ -n "$old_target" ]]; then
        ln -sfn -- "$old_target" "$root/.current-rollback-$$"
        mv -Tf -- "$root/.current-rollback-$$" "$current"
        /usr/bin/systemctl --user restart avi-site-testing.service || true
    fi
    exit 1
fi
printf 'deployed %s\n' "$sha"
