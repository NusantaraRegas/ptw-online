#!/bin/sh
# Turns PTW_ADMIN_ALLOW_CIDRS (comma-separated IPv4/IPv6 networks) into an nginx access-control
# snippet for the /api/v1/admin/ location. Empty means no network restriction, so a development
# stack keeps working; production sets the office and VPN ranges.
set -eu

out=/tmp/ptw-admin-allow.conf
: > "$out"
cidrs="${PTW_ADMIN_ALLOW_CIDRS:-}"
[ -z "$cidrs" ] && exit 0

old_ifs="$IFS"
IFS=','
for cidr in $cidrs; do
  cidr="$(printf '%s' "$cidr" | tr -d ' ')"
  [ -z "$cidr" ] && continue
  case "$cidr" in
    *[!0-9a-fA-F:./]*)
      echo "PTW_ADMIN_ALLOW_CIDRS contains an invalid network: $cidr" >&2
      exit 1
      ;;
  esac
  printf 'allow %s;\n' "$cidr" >> "$out"
done
IFS="$old_ifs"
printf 'deny all;\n' >> "$out"
