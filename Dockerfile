# Build a due stadi: l'SDK .NET pesa ~2,5 GB estratto e serve solo a
# compilare. L'immagine finale porta il runtime ASP.NET e basta.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# I file di progetto per primi, il codice dopo: cambiano di rado, quindi il
# layer del restore resta in cache anche quando cambia il codice, e un
# rebuild dopo una modifica non riscarica tutti i pacchetti NuGet.
#
# Directory.Packages.props non e' opzionale: il progetto usa la gestione
# centralizzata delle versioni, e senza quel file nessun PackageReference
# ha una versione e il restore fallisce.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/FrasiSquisite.Shared/FrasiSquisite.Shared.csproj src/FrasiSquisite.Shared/
COPY src/FrasiSquisite.Domain/FrasiSquisite.Domain.csproj src/FrasiSquisite.Domain/
COPY src/FrasiSquisite.Server/FrasiSquisite.Server.csproj src/FrasiSquisite.Server/

# Si restaura il solo progetto del server, non la solution: FrasiSquisite.App
# ha per bersaglio net10.0-android e su Linux, senza il workload MAUI,
# fallirebbe il restore fermando tutta la build. Il server tira dietro solo
# Domain e Shared, che sono le sue uniche dipendenze di progetto.
RUN dotnet restore src/FrasiSquisite.Server/FrasiSquisite.Server.csproj

COPY src/FrasiSquisite.Shared/ src/FrasiSquisite.Shared/
COPY src/FrasiSquisite.Domain/ src/FrasiSquisite.Domain/
COPY src/FrasiSquisite.Server/ src/FrasiSquisite.Server/

RUN dotnet publish src/FrasiSquisite.Server/FrasiSquisite.Server.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# L'immagine definisce gia' un utente non privilegiato (app, uid 1654):
# si usa quello invece di girare da root. Il server non scrive nulla su
# disco, quindi non serve alcun permesso in piu'.
USER $APP_UID

# In ascolto su tutte le interfacce del container, non su localhost:
# altrimenti la porta pubblicata da compose non raggiungerebbe nessuno.
# Resta una variabile d'ambiente proprio per poterla cambiare da compose
# senza ricostruire l'immagine.
ENV ASPNETCORE_URLS=http://+:5000

EXPOSE 5000

# Nessun HEALTHCHECK: l'immagine runtime non ha curl ne' wget, e aggiungerli
# per questo solo scopo la ingrasserebbe. Il server espone /health e il
# controllo si fa da fuori, dove c'e' gia' Uptime Kuma.

ENTRYPOINT ["dotnet", "FrasiSquisite.Server.dll"]
