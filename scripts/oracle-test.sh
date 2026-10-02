#!/usr/bin/env bash
# Runs the PL/SQL tests of package PKG_LOAN_SCORING inside the oracle container.
# Exit code is non-zero if any test fails (WHENEVER SQLERROR EXIT FAILURE in the script).
#
# Usage: scripts/oracle-test.sh   (the container must be healthy)
set -euo pipefail

cd "$(dirname "$0")/.."

# The test file is passed via stdin (-T disables the TTY), so oracle/tests does not need
# to be mounted into the container. Login and password come from the CONTAINER's
# environment (single quotes), so the password never lands in the host shell history.
# -L: on a wrong password, fail immediately instead of prompting again.
docker compose exec -T oracle bash -c \
    'sqlplus -s -L "${APP_USER}/${APP_USER_PASSWORD}@//localhost:1521/FREEPDB1"' \
    < oracle/tests/test_pkg_loan_scoring.sql
