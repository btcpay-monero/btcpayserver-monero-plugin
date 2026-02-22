#!/bin/sh
set -e

dotCover cover \
  --target-working-directory . \
  --snapshot-output /coverage/dotCover.IntegrationTests.dcvr \
  -- dotnet test -c Release . --no-build
#  TODO: apply filters
#  --filters="-:Assembly=BTCPayServer.Plugins.IntegrationTests;-:Assembly=testhost;-:Assembly=BTCPayServer;-:Assembly=ExchangeSharp;-:Assembly=BTCPayServer.Tests;-:Assembly=BTCPayServer.Client;-:Assembly=BTCPayServer.Abstractions;-:Assembly=BTCPayServer.Data;-:Assembly=BTCPayServer.Common;-:Assembly=BTCPayServer.Logging;-:Assembly=BTCPayServer.Rating;-:Assembly=Dapper;-:Assembly=Serilog.Extensions.Logging;-:Class=AspNetCoreGeneratedDocument.*"

dotCover merge \
  --snapshot-source /coverage/dotCover.IntegrationTests.dcvr,/coverage/dotCover.UnitTests.dcvr \
  --snapshot-output /coverage/mergedCoverage.dcvr

dotCover report \
  --snapshot-source /coverage/mergedCoverage.dcvr \
  --xml-report-output /coverage/dotcover.xml \
  --log-file /coverage/logfile.txt