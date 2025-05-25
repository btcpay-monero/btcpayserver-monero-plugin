#!/bin/sh
set -e

FILTERS=" "
if [ ! -z "$TEST_FILTERS" ]; then
FILTERS="--filter $TEST_FILTERS"
fi

dotnet test -c ${CONFIGURATION_NAME} $FILTERS --no-build -v n --logger "console;verbosity=normal" /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura /p:CoverletOutput=/coverage/coverage.cobertura.xml /p:MergeWith=/coverage/coverage.json< /dev/null