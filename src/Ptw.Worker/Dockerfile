FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet publish src/Ptw.Worker/Ptw.Worker.csproj -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
# The worker renders and writes official permit packages. Fonts and the logo are embedded in the
# assembly, so no system font package is needed and rendering stays identical on any host.
USER root
RUN mkdir -p /var/lib/ptw/generated-documents \
    && chown -R "$APP_UID:$APP_UID" /var/lib/ptw/generated-documents
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Ptw.Worker.dll"]
