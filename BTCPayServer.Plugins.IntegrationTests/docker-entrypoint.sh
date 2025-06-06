#!/bin/sh
set -e

dotCover cover-dotnet \
  --TargetArguments="test -c ${CONFIGURATION_NAME} $FILTERS . --no-build" \
  --Output=/coverage/dotCover.IntegrationTests.output.dcvr \
  --filters="-:Assembly=BTCPayServer.Plugins.IntegrationTests;-:Assembly=testhost;-:Assembly=BTCPayServer;-:Assembly=ExchangeSharp;-:Assembly=BTCPayServer.Tests;-:Assembly=BTCPayServer.Client;-:Assembly=BTCPayServer.Abstractions;-:Assembly=BTCPayServer.Data;-:Assembly=BTCPayServer.Common;-:Assembly=BTCPayServer.Logging;-:Assembly=BTCPayServer.Rating;-:Assembly=Dapper;-:Assembly=Serilog.Extensions.Logging;-:Class=AspNetCoreGeneratedDocument.*"

dotCover merge \
  --Source=/coverage/dotCover.IntegrationTests.output.dcvr,/coverage/dotCover.UnitTests.output.dcvr \
  --Output=/coverage/mergedCoverage.dcvr

dotCover report \
  --Source=/coverage/mergedCoverage.dcvr \
  --ReportType=HTML \
  --Output=/coverage/mergedCoverage.html \
  --ReportType=DetailedXML \
  --Output=/coverage/dotcover.xml
  
dotCover report \
  --Source=/coverage/dotCover.UnitTests.output.dcvr \
  --ReportType=HTML \
  --Output=/coverage/unitCoverage.html
  
dotCover report \
  --Source=/coverage/dotCover.IntegrationTests.output.dcvr \
  --ReportType=HTML \
  --Output=/coverage/integrationCoverage.html