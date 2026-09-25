#!/bin/sh
# Turns PTW_TRUSTED_PROXY_CIDRS (comma-separated networks of the upstream reverse proxy that
# terminates TLS for the public host, OPN-009 Amendemen 1) into two nginx snippets:
#   /tmp/ptw-trusted-proxies.conf  real_ip directives so $remote_addr becomes the real client and
#                                  rate limits, allow-lists, and the login journal key on it;
#   /tmp/ptw-trusted-proxy-geo.conf a geo map flagging connections that come from the proxy, which
#                                  is the only source whose X-Forwarded-Proto is believed.
# Empty means no proxy is trusted: every plain-HTTP request keeps redirecting to https.
set -eu

real_ip=/tmp/ptw-trusted-proxies.conf
geo=/tmp/ptw-trusted-proxy-geo.conf
: > "$real_ip"
printf 'geo $realip_remote_addr $ptw_trusted_proxy {\n    default 0;\n' > "$geo"
cidrs="${PTW_TRUSTED_PROXY_CIDRS:-}"

old_ifs="$IFS"
IFS=','
for cidr in $cidrs; do
  cidr="$(printf '%s' "$cidr" | tr -d ' ')"
  [ -z "$cidr" ] && continue
  case "$cidr" in
    *[!0-9a-fA-F:./]*)
      echo "PTW_TRUSTED_PROXY_CIDRS contains an invalid network: $cidr" >&2
      exit 1
      ;;
  esac
  printf 'set_real_ip_from %s;\n' "$cidr" >> "$real_ip"
  printf '    %s 1;\n' "$cidr" >> "$geo"
done
IFS="$old_ifs"
printf '}\n' >> "$geo"

if [ -s "$real_ip" ]; then
  printf 'real_ip_header X-Forwarded-For;\nreal_ip_recursive on;\n' >> "$real_ip"
fi
