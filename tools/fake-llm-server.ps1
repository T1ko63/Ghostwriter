<#
.SYNOPSIS
    Kleiner Test-Server, der auf jede Anfrage mit einem festen HTTP-Fehler antwortet (Standard 503).

.DESCRIPTION
    Nur für die manuelle Prüfung des Ausweich-Anbieters (fallback_provider), siehe
    docs/refactoring-report.md, Abschnitt "Ausweich-Anbieter prüfen". Lauscht ausschließlich auf 127.0.0.1,
    braucht keine Administratorrechte, liest nichts und speichert nichts. Beenden mit Strg+C.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\fake-llm-server.ps1
    powershell -ExecutionPolicy Bypass -File tools\fake-llm-server.ps1 -Status 429
#>
param(
    [int]$Port = 8089,
    [ValidateSet(429, 500, 502, 503)]
    [int]$Status = 503
)

$reason = @{ 429 = 'Too Many Requests'; 500 = 'Internal Server Error'; 502 = 'Bad Gateway'; 503 = 'Service Unavailable' }[$Status]
$body = '{"error":{"message":"fake-llm-server: simulated ' + $Status + '","type":"server_error"}}'
$bytes = [Text.Encoding]::UTF8.GetBytes(
    "HTTP/1.1 $Status $reason`r`nContent-Type: application/json`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n$body")

$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
$listener.Start()
Write-Host "Antwortet auf http://127.0.0.1:$Port/ mit $Status $reason. Beenden mit Strg+C."
try {
    while ($true) {
        $client = $listener.AcceptTcpClient()
        try {
            $stream = $client.GetStream()
            $stream.ReadTimeout = 2000
            # Anfrage vollständig lesen und verwerfen (sonst setzt das Schließen die Verbindung zurück, und Kuroko
            # sähe "keine Verbindung" statt des Statuscodes). Der Inhalt wird weder angezeigt noch gespeichert.
            $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $false, 1024, $true)
            $first = $reader.ReadLine()
            $length = 0
            while ($line = $reader.ReadLine()) {
                if ($line -match '^Content-Length:\s*(\d+)') { $length = [int]$Matches[1] }
            }
            $buffer = [char[]]::new(1)
            while ($length -gt 0 -and $reader.Read($buffer, 0, 1) -gt 0) {
                $length -= [Text.Encoding]::UTF8.GetByteCount($buffer)
            }
            $stream.Write($bytes, 0, $bytes.Length)
            Write-Host "$(Get-Date -Format HH:mm:ss)  $first  ->  $Status"
        }
        catch { Write-Host "$(Get-Date -Format HH:mm:ss)  Verbindung abgebrochen: $($_.Exception.Message)" }
        finally { $client.Close() }
    }
}
finally { $listener.Stop() }
