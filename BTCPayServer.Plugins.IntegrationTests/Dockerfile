FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS builder

RUN apt-get update && apt-get install -y --no-install-recommends chromium-driver \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /source
COPY ../submodules/btcpayserver/nuget.config nuget.config
COPY ../submodules/btcpayserver/Build/Common.csproj Build/Common.csproj
COPY ../submodules/btcpayserver/BTCPayServer.Abstractions/BTCPayServer.Abstractions.csproj BTCPayServer.Abstractions/BTCPayServer.Abstractions.csproj
COPY ../submodules/btcpayserver/BTCPayServer/BTCPayServer.csproj BTCPayServer/BTCPayServer.csproj
COPY ../submodules/btcpayserver/BTCPayServer.Common/BTCPayServer.Common.csproj BTCPayServer.Common/BTCPayServer.Common.csproj
COPY ../submodules/btcpayserver/BTCPayServer.Rating/BTCPayServer.Rating.csproj BTCPayServer.Rating/BTCPayServer.Rating.csproj
COPY ../submodules/btcpayserver/BTCPayServer.Data/BTCPayServer.Data.csproj BTCPayServer.Data/BTCPayServer.Data.csproj
COPY ../submodules/btcpayserver/BTCPayServer.Client/BTCPayServer.Client.csproj BTCPayServer.Client/BTCPayServer.Client.csproj
RUN cd BTCPayServer && dotnet restore
COPY ../submodules/btcpayserver/BTCPayServer.Common/. BTCPayServer.Common/.
COPY ../submodules/btcpayserver/BTCPayServer.Rating/. BTCPayServer.Rating/.
COPY ../submodules/btcpayserver/BTCPayServer.Data/. BTCPayServer.Data/.
COPY ../submodules/btcpayserver/BTCPayServer.Client/. BTCPayServer.Client/.
COPY ../submodules/btcpayserver/BTCPayServer.Abstractions/. BTCPayServer.Abstractions/.
COPY ../submodules/btcpayserver/BTCPayServer/. BTCPayServer/.
COPY ../submodules/btcpayserver/Build/Version.csproj Build/Version.csproj

ENV SCREEN_HEIGHT 600 \
    SCREEN_WIDTH 1200

COPY . .

ARG CONFIGURATION_NAME=Release
ARG MONERO_PLUGIN_FOLDER=/root/.btcpayserver/Plugins/BTCPayServer.Plugins.Monero/
RUN mkdir -p ${MONERO_PLUGIN_FOLDER}
RUN cd Plugins/Monero && dotnet build BTCPayServer.Plugins.Monero.sln --configuration ${CONFIGURATION_NAME} /p:RazorCompileOnBuild=true --output ${MONERO_PLUGIN_FOLDER}
RUN cd BTCPayServer.Plugins.IntegrationTests && dotnet build --configuration ${CONFIGURATION_NAME} /p:CI_TESTS=true /p:RazorCompileOnBuild=true
RUN dotnet tool install --global Microsoft.Playwright.CLI
RUN dotnet tool install --global JetBrains.DotCover.CommandLineTools --version 2025.1.6
ENV PATH="$PATH:/root/.dotnet/tools"
RUN playwright install chromium --with-deps
WORKDIR /source/BTCPayServer.Plugins.IntegrationTests
ENV CONFIGURATION_NAME=${CONFIGURATION_NAME}
ENTRYPOINT ["./docker-entrypoint.sh"]