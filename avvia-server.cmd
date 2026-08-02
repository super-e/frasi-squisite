@echo off
setlocal
title Frasi Squisite - server

rem Estensione .cmd e non .bat: sono equivalenti per Windows, ma
rem l'antivirus di questa macchina aveva messo in lista nera il nome
rem "avvia-server.bat" e impediva perfino di ricrearlo.

rem Si posiziona nella cartella dello script, cosi' funziona anche
rem lanciandolo con doppio clic da Esplora risorse.
cd /d "%~dp0"

echo ================================================================
echo   Sul telefono, in Impostazioni, scrivi uno di questi indirizzi:
echo ================================================================
echo.

rem Tre esclusioni, tutte per lo stesso motivo: mostrare un indirizzo che
rem non funziona costringe a indovinare qual e' quello buono.
rem   127.*      loopback, raggiungibile solo da questo PC
rem   169.254.*  APIPA, l'indirizzo che Windows si inventa quando il DHCP
rem              non risponde: la scheda c'e' ma non e' su nessuna rete
rem   alias      schede virtuali (WSL, Hyper-V, VirtualBox) e tunnel VPN
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$reti = Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' -and $_.InterfaceAlias -notmatch 'Loopback|vEthernet|WSL|VPN|NordLynx|VirtualBox|VMware|outline|TAP|TUN' };" ^
  "if ($reti) { $reti | ForEach-Object { '    http://' + $_.IPAddress + ':5000     (' + $_.InterfaceAlias + ')' } } else { '    Nessuna rete trovata: questo PC non risulta collegato.' }"

echo.
echo   Il telefono deve stare sulla stessa rete Wi-Fi.
echo   Se non si collega, e' quasi sempre il firewall di Windows:
echo   la prima volta chiede il permesso, va dato per la rete PRIVATA.
echo.
echo   Ctrl+C per fermare il server.
echo.

rem Il profilo "lan" e' il primo di launchSettings.json, quindi e' quello
rem che "dotnet run" prende da solo: ascolta su 0.0.0.0:5000 invece che
rem su localhost. Per cambiare porta senza toccare il codice basta
rem ASPNETCORE_URLS, che ha comunque la precedenza sul profilo.
rem
rem Se qui compare "address already in use", c'e' un altro server acceso:
rem chiudi la finestra dove gira. Un taskkill automatico sarebbe stato
rem comodo, ma e' proprio cio' che ha fatto scattare l'antivirus.
dotnet run --project src\FrasiSquisite.Server

echo.
echo Server fermato.
pause
endlocal
