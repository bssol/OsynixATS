FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/Osynix.Ats/ src/Osynix.Ats/
RUN dotnet publish src/Osynix.Ats/Osynix.Ats.csproj -c Release -o /app/publish
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN dotnet Osynix.Ats.dll --install-browser && chmod -R a+rX /ms-playwright
RUN mkdir -p App_Data && chown -R app:app /app
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet","Osynix.Ats.dll"]
