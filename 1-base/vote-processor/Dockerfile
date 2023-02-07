FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build

RUN apt-get update && apt-get install -y --no-install-recommends clang zlib1g-dev

WORKDIR /source

COPY ["vote-processor/vote-processor.csproj", "vote-processor/"]
COPY ["../common/common.csproj", "common/"]
COPY ["Directory.Build.props", "./"]
COPY . .
RUN dotnet publish -c release -r linux-x64 -o /app

FROM mcr.microsoft.com/dotnet/runtime-deps:7.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["/app/vote-processor"]