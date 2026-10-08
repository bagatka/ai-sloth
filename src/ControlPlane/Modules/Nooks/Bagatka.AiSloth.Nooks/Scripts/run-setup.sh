#!/bin/bash
set -u
log=/var/log/aisloth/setup.log
mkdir -p /var/log/aisloth
: > "$log"
run() {
    status=0
    for script in "$@"; do
        case "$script" in
            */resume) limit=5m ;;
            *) limit=30m ;;
        esac
        echo "==> $script"
        if [ -x "/work/$script" ]; then
            (cd "/work/${script%.agents/*}" && timeout "$limit" "/work/$script")
        else
            (cd "/work/${script%.agents/*}" && timeout "$limit" bash "/work/$script")
        fi
        code=$?
        if [ "$code" -eq 124 ]; then
            echo "==> $script ran longer than $limit and was stopped"
        elif [ "$code" -ne 0 ]; then
            echo "==> $script failed with exit code $code"
        fi
        if [ "$code" -ne 0 ] && [ "$status" -eq 0 ]; then
            status=$code
        fi
    done
    return "$status"
}
run "$@" < /dev/null >> "$log" 2>&1 &
worker=$!
tail -n +1 -f --sleep-interval=0.2 --pid="$worker" "$log"
wait "$worker"
