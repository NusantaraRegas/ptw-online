FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet publish src/Ptw.Api/Ptw.Api.csproj -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    # OpenLDAP client library required by System.DirectoryServices.Protocols for AD login.
    && (apt-get install -y --no-install-recommends libldap2 \
        || apt-get install -y --no-install-recommends libldap-2.5-0) \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /var/lib/ptw/attachments /var/lib/ptw/generated-documents /var/lib/ptw/data-protection \
    && chown -R "$APP_UID:$APP_UID" /var/lib/ptw/attachments /var/lib/ptw/generated-documents /var/lib/ptw/data-protection
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Ptw.Api.dll"]
