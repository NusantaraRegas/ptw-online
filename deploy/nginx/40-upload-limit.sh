#!/bin/sh
set -eu

upload_limit="${PTW_UPLOAD_MAX_BODY:-1m}"
numeric_limit="${upload_limit%[kKmMgG]}"
case "$numeric_limit" in
  ''|0|*[!0-9]*)
    echo "PTW_UPLOAD_MAX_BODY must be a positive nginx size such as 11m" >&2
    exit 1
    ;;
esac
case "$upload_limit" in
  "$numeric_limit"|"$numeric_limit"[kKmMgG]) ;;
  *)
    echo "PTW_UPLOAD_MAX_BODY must be a positive nginx size such as 11m" >&2
    exit 1
    ;;
esac

printf 'client_max_body_size %s;\n' "$upload_limit" > /tmp/ptw-upload-limit.conf
