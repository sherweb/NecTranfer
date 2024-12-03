FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
ARG AZURE_NUGET_PWD
ARG APPNAME

ENV DOTNET_SYSTEM_NET_HTTP_USESOCKETSHTTPHANDLER=0

WORKDIR /workdir

COPY ./ ./

RUN sed -i "s/NUGET_PWD/${AZURE_NUGET_PWD}/g" .build/NuGet.config && \
 dotnet restore "${APPNAME}" -r linux-musl-x64 --configfile .build/NuGet.config && \
 dotnet publish "${APPNAME}" -r linux-musl-x64 -p:PublishTrimmed=false --self-contained -c Release -o ./output

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine
ARG APPNAME
ARG USER=app
WORKDIR /app

COPY --from=build workdir/output /app
COPY .build/*.crt /usr/local/share/ca-certificates/

RUN apk upgrade --no-cache && \
    apk add --no-cache sudo icu-libs wget openssl bash tzdata ca-certificates && \
    cp /usr/share/zoneinfo/America/Toronto /etc/localtime && \
    mv "$(basename ${APPNAME})" applicationToStart && \
    chmod +x applicationToStart && \
    update-ca-certificates && \
    mkdir -p /etc/sudoers.d && \
    echo "$USER ALL=(ALL) NOPASSWD: ALL" > /etc/sudoers.d/$USER && \
    chmod 0440 /etc/sudoers.d/$USER && \
    chown -R $USER:$USER /app

EXPOSE 8080

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 

USER $USER

CMD ["./applicationToStart"]
