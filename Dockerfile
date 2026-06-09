# syntax=docker/dockerfile:1

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Copy the repo (the WPF app + tests are excluded via .dockerignore). Only the Web project's
# graph (Core -> Network -> Photon parser chain) is restored/built.
COPY . .
RUN dotnet publish src/StatisticsAnalysisTool.Web/StatisticsAnalysisTool.Web.csproj \
        -c Release -o /app

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# libpcap for packet capture. The libpcap-net binding loads the unversioned "libpcap.so", which
# only the -dev package ships, so symlink it to the runtime library that libpcap0.8 installs.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libpcap0.8 \
    && ln -sf "libpcap.so.0.8" "/usr/lib/$(uname -m)-linux-gnu/libpcap.so" \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_URLS=http://0.0.0.0:8087
EXPOSE 8087

# NOTE: live capture needs the NET_RAW (and usually NET_ADMIN) capability and host networking
# to see game traffic:
#   docker run --rm --network host --cap-add NET_RAW --cap-add NET_ADMIN sat-web
ENTRYPOINT ["dotnet", "StatisticsAnalysisTool.Web.dll"]
