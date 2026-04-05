#!/bin/sh
# Initialize rules from seed file if main file is empty

DATA_DIR="/app/data"
RULES_FILE="$DATA_DIR/rules.json"
SEED_FILE="/app/seed-rules.json"

mkdir -p "$DATA_DIR"

# If rules.json doesn't exist or is empty, copy from seed
if [ ! -f "$RULES_FILE" ] || [ ! -s "$RULES_FILE" ]; then
    if [ -f "$SEED_FILE" ]; then
        cp "$SEED_FILE" "$RULES_FILE"
        echo "Initialized rules from seed file"
    fi
fi
