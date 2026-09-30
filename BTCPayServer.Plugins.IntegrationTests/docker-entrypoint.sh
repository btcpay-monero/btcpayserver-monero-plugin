#!/bin/sh
set -e

dotnet "bin/${CONFIGURATION_NAME}/net10.0/BTCPayServer.Plugins.IntegrationTests.dll" \
  --output Detailed \
  --coverlet \
  --coverlet-output-format cobertura \
  --coverlet-include "[BTCPayServer.Plugins.Monero*]*"

mkdir -p /TestResults/coverage/integration
mv "bin/${CONFIGURATION_NAME}/net10.0/TestResults"/coverage.cobertura.*.xml \
   /TestResults/coverage/integration/coverage.cobertura.xml

reportgenerator \
  -reports:"/TestResults/coverage/unit/coverage.cobertura.xml;/TestResults/coverage/integration/coverage.cobertura.xml" \
  -targetdir:"/TestResults/coverage/merged" \
  -reporttypes:"HtmlSummary;Cobertura"