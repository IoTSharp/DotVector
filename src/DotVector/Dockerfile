# DotVector gRPC Server — multi-stage Dockerfile
# 构建：docker build -f src/DotVector/Dockerfile -t dotvector:dev .
# 运行：docker run -p 5180:5180 -v dotvector-data:/data dotvector:dev

# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 复制必要的工程文件（利用 docker 层缓存）
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY protos/ ./protos/
COPY src/DotVector.Core/ ./src/DotVector.Core/
COPY src/DotVector/ ./src/DotVector/

RUN dotnet restore src/DotVector/DotVector.csproj
RUN dotnet publish src/DotVector/DotVector.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

ARG VERSION=dev
LABEL org.opencontainers.image.title="DotVector" \
      org.opencontainers.image.version="$VERSION" \
      org.opencontainers.image.description="DotVector gRPC server"

ENV DOTVECTOR_DATA_DIR=/data \
    DOTVECTOR_PORT=5180 \
    ASPNETCORE_URLS=

EXPOSE 5180
VOLUME ["/data"]

ENTRYPOINT ["dotnet", "DotVector.dll"]
