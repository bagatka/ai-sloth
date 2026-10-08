#!/bin/sh
export LC_ALL=C
cd /work 2>/dev/null || exit 0
for kind in setup resume; do
    for script in .agents/"$kind" */.agents/"$kind"; do
        if [ -f "$script" ]; then printf '%s\n' "$script"; fi
    done
done
