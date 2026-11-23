# Docker (tuy chon):  docker build -f deploy/Dockerfile -t kholanh-server .   (chay o thu muc goc repo)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/KhoLanh.Server -c Release -o /out
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /out .
ENV KHOLANH_DATA=/data
VOLUME /data
EXPOSE 47820
ENTRYPOINT ["dotnet", "kholanh-server.dll"]
