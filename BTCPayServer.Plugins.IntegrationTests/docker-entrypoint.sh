#!/bin/sh
set -e

dotnet test -c "${CONFIGURATION_NAME}" --no-build -v n /p:CollectCoverage=true /p:CoverletOutput=/coverage/integration/ /p:CoverletOutputFormat=cobertura /p:Include="[BTCPayServer.Plugins.Monero*]*"

reportgenerator \
  -reports:"/coverage/unit/coverage.cobertura.xml;/coverage/integration/coverage.cobertura.xml" \
  -targetdir:"/coverage/merged" \
  -reporttypes:"HtmlSummary;Cobertura"