#!/usr/bin/env bash
#
# run-upload.sh
#
# Runs the attendance report uploader for yesterday's date.
# Usage: ./run-upload.sh [path-to-file]
#   Defaults to ./transation_data.xlsx if no path is given.

set -euo pipefail

FILE_PATH="${1:-./transaction_data.xlsx}"

# Compute yesterday's date in YYYY-MM-DD format.
# macOS/BSD date uses -v-1d; GNU/Linux date uses -d "yesterday".
if date -v-1d >/dev/null 2>&1; then
    YESTERDAY=$(date -v-1d +%Y-%m-%d)   # macOS/BSD
else
    YESTERDAY=$(date -d "yesterday" +%Y-%m-%d)   # Linux
fi

echo "Uploading '${FILE_PATH}' for date ${YESTERDAY}..."

dotnet run -- "${FILE_PATH}" "${YESTERDAY}"