# AIS.NET Projects

| Package                                                           | Status                                                                                                                                               |
| ----------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| [Ais.Net](https://github.com/ais-dotnet/Ais.Net)                  | [![#](https://img.shields.io/nuget/v/Ais.Net.svg)](https://www.nuget.org/packages/Ais.Net/)                                                          |
| [Ais.Net.Models](https://github.com/ais-dotnet/Ais.Net.Models)    | [![#](https://img.shields.io/nuget/v/Ais.Net.Models.svg)](https://www.nuget.org/packages/Ais.Net.Models/)                                            |
| Ais.Net.Receiver                                                  | [![#](https://img.shields.io/nuget/v/Ais.Net.Receiver.svg)](https://www.nuget.org/packages/Ais.Net.Receiver/)                                        |
| Ais.Net.Receiver.Storage.Azure.Blob                               | [![#](https://img.shields.io/nuget/v/Ais.Net.Receiver.Storage.Azure.Blob.svg)](https://www.nuget.org/packages/Ais.Net.Receiver.Storage.Azure.Blob/)  |

The AIS.NET project contains a series of layers, from a low-level high performance NMEA AIS sentence decoder, to a rich high-level C# 9.0 models of AIS message types, a receiver component that can listen to TCP streams of NMEA sentences and expose them as an `IObservable<string>` of raw sentences or an decoded `IObservable<IAisMessage>`, and finally a Storage Client implementation to persisting the raw NMEA sentence stream to Azure Blob storage for future processing.

![https://github.com/ais-dotnet](https://endjincdn.blob.core.windows.net/assets/ais-dotnet-project-layers.png)

While `Ais.Net` aims for zero allocation, `Ais.Net.Models` and `Ais.Net.Receiver` aim for convenience of a higher level programming model, while still being efficient. `Ais.Net.Receiver` has run on a Raspberry Pi 4 as a systemd service robustly for many years, and uses very little CPU and Memory to ingest all the Norwegian Coastal Administration in real-time.

```
PID USER      PR  NI    VIRT    RES    SHR S  %CPU  %MEM     TIME+ COMMAND
679 root      20   0  287176  71536  43744 S   0.3   1.8  77:21.28 dotnet
```

```
:~ $ sudo systemctl status aisr
● aisr.service - aisr
   Loaded: loaded (/lib/systemd/system/aisr.service; enabled; vendor preset: enabled)
   Active: active (running) since Mon 2025-03-03 12:17:19 GMT; 6 days ago
 Main PID: 679 (dotnet)
    Tasks: 16 (limit: 4915)
   CGroup: /system.slice/aisr.service
           └─679 /home/pi/.dotnet/dotnet /home/pi/aisr/Ais.Net.Receiver.Host.Console.dll
```

It is also now available as a docker container for ease of install and management.

# Ais.Net.Receiver

A simple .NET AIS Receiver for capturing the Norwegian Coastal Administration's marine Automatic Identification System (AIS) [AIVDM/AIVDO](https://gpsd.gitlab.io/gpsd/AIVDM.html) NMEA message network data (available under [Norwegian license for public data (NLOD)](https://data.norge.no/nlod/en/2.0)) and persisting in Microsoft Azure Blob Storage.

The [Norwegian Coastal Administration provide a TCP endpoint](https://ais.kystverket.no/) (`153.44.253.27:5631`) for broadcasting their raw AIS AIVDM/AIVDO sentences, captured by over 50 base stations, and covers the area 40-60 nautical miles from the Norwegian coastline.

This project contains a [NmeaReceiver](https://github.com/ais-dotnet/Ais.Net.Receiver/blob/master/Solutions/Ais.Net.Receiver/Receiver/NmeaReceiver.cs) which consumes the raw NetworkStream, a [NmeaToAisMessageTypeProcessor](https://github.com/ais-dotnet/Ais.Net.Receiver/blob/master/Solutions/Ais.Net.Receiver/Parser/NmeaToAisMessageTypeProcessor.cs), which can decode the raw sentences into `IAisMessage`, and [ReceiverHost](https://github.com/ais-dotnet/Ais.Net.Receiver/blob/master/Solutions/Ais.Net.Receiver/Receiver/ReceiverHost.cs) which manages the process and exposes an `IObservable<string>` for raw sentences and an `IObservable<IAisMessage>` for decoded messages. `ReceiverHost` can be hosted in a console application or other runtime environments like [Polyglot Notebooks](https://github.com/dotnet/interactive) or even [WASM](#running-as-wasm).

The project also includes a [demo console](https://github.com/ais-dotnet/Ais.Net.Receiver/blob/master/Solutions/Ais.Net.Receiver.Host.Console/Program.cs) which shows how the various pieces can fit together, including subscribing to the `IObservable<string>` and `IObservable<IAisMessage>` streams and displaying the results or batch the AIVDM/AIVDO sentences and write them to Azure Blob Storage using the [Append Blob](https://docs.microsoft.com/en-us/rest/api/storageservices/append-block) feature, to create timestamped hour-long rolling logs.

The purpose of this application is to provide sample data for [Ais.Net](https://github.com/ais-dotnet/Ais.Net) - the .NET Standard, high performance, zero allocation AIS decoder. The majority of raw AIS data is only available via commercial sources, and thus creating AIS datasets large enough to test / benchmark [Ais.Net](https://github.com/ais-dotnet/Ais.Net) is almost impossible. 

The Norwegian Coastal Administration TCP endpoint produces:
 
- ~2.9 KB per second 
- ~10.3 MB per hour 
- ~248 MB per day 
- ~1.7 GB per week 
- ~7 GB per month
- ~81.4 GB per year

## Azure Blob Storage Taxonomy

The AIS data is stored using the following taxonomy

`<USER DEFINED CONTAINER NAME>/raw/yyyy/MM/dd/yyyyMMddTHH.nm4`

An example directory listing, with a user defined container name of `nmea-ais` would look as follows:

```
\---nmea-ais
    \---raw
        \---2021
            \---07
                +---12
                | 20210712T00.nm4 |
                | 20210712T01.mm4 |
                | 20210712T02.nm4 |
                | 20210712T03.nm4 |
                | 20210712T04.nm4 |
                | 20210712T05.nm4 |
                | 20210712T06.nm4 |
                | 20210712T07.nm4 |
                | 20210712T08.nm4 |
                | 20210712T09.nm4 |
                | 20210712T10.nm4 |
                | 20210712T11.nm4 |
                | 20210712T12.nm4 |
                | 20210712T13.nm4 |
                | 20210712T14.nm4 |
                | 20210712T15.nm4 |
                | 20210712T16.nm4 |
                | 20210712T17.nm4 |
                | 20210712T18.nm4 |
                | 20210712T19.nm4 |
                | 20210712T20.nm4 |
                | 20210712T21.nm4 |
                | 20210712T22.nm4 |
                | 20210712T23.nm4 |
                +---20210713
                | 20210713T00.nm4 |
```

## To Run

Update the values in the `appsettings.json` file:

```json
{
  "Ais": {
    "Connection": {
      "Host": "153.44.253.27",
      "Port": 5631,
      "Retry": {
        "Attempts": 5,
        "Periodicity": "00:00:00:01"
      }
    }
  },
  "Storage": {
    "enableCapture": true,
    "connectionString": "<YOUR AZURE STORAGE CONNECTION STRING>",
    "containerName": "nmea-ais",
    "writeBatchSize": 500
  }
}
```

See [Configuration](#configuration) for the full schema and every available setting.

From the command line, either host will do — the console host for interactive use, the worker for
running as a service:

```bash
dotnet Ais.Net.Receiver.Host.Console.dll
dotnet Ais.Net.Receiver.Host.Worker.dll
```

### Local development with the Aspire AppHost

The AppHost runs the worker alongside an [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite)
container, so capture works on a fresh clone with no Azure account and no secrets. Docker must be
running.

```bash
dotnet run --project Solutions/Ais.Net.Receiver.AppHost
```

It injects `Storage__ConnectionString` (pointing at the emulator) and `Storage__EnableCapture=true`
into the worker, and waits for Azurite to be ready first. Captured NMEA lands in the `nmea-ais-dev`
container and persists across restarts in the `ais-azurite-data` volume. Traces, metrics and logs
appear in the Aspire dashboard, whose login link the AppHost prints on startup.

`enableCapture` is `false` in the tracked `appsettings.json`, because capture needs a connection
string to go with it. To capture against a real storage account outside the AppHost, keep that
connection string out of source control with user secrets rather than editing `appsettings.json`:

```bash
dotnet user-secrets set "Storage:EnableCapture" "true" --project Solutions/Ais.Net.Receiver.Host.Worker
dotnet user-secrets set "Storage:ConnectionString" "<connection string>" --project Solutions/Ais.Net.Receiver.Host.Worker
```

Environment variables still take precedence over user secrets, so what a container or systemd unit
injects continues to win.

## Running Tests

To run the tests, use the following command:

```bash
dotnet test --solution Solutions/Ais.Net.Receiver.slnx
```

The test project is configured to use the `Microsoft.Testing.Platform` runner. Note that MTP does not
accept the older VSTest-era switches — passing `--nologo`, `--logger` or `--test-adapter-path` makes
the run report `Zero tests ran` rather than an unrecognised-argument error.

### Integration tests

Tests categorised `Integration` run against a real Azure Blob Storage endpoint, provided by an
[Azurite](https://github.com/Azure/Azurite) container started through
[Testcontainers](https://dotnet.testcontainers.org/). **They need a working Docker daemon.** One
container is shared by the whole test process and started only on demand, so a unit-only run costs
nothing:

```bash
cd Solutions/Ais.Net.Receiver.Tests

dotnet run -f net10.0                                                  # everything
dotnet run -f net10.0 -- --filter "TestCategory=Integration"           # integration only
dotnet run -f net10.0 -- --filter "TestCategory!=Integration"          # unit only, no container
```

When Docker is unavailable these tests report as inconclusive rather than failing. Any *other*
startup problem — an unresolvable image tag, a failed pull, a port conflict — fails the build, because
a broken container reported as a skip is indistinguishable from having no coverage at all.

The following environment variables tune this:

| Variable | Default | Purpose |
| --- | --- | --- |
| `AIS_TEST_REQUIRE_INTEGRATION` | unset | Truthy: never skip, so missing Docker fails. Set in CI. |
| `AIS_TEST_AZURITE_IMAGE` | `mcr.microsoft.com/azure-storage/azurite:latest` | Pin the image, e.g. to work around a bad release. |
| `AIS_TEST_AZURITE_SKIP_API_VERSION_CHECK` | `true` | Falsy: drop `--skipApiVersionCheck` (see below). |
| `AIS_TEST_AZURITE_STARTUP_TIMEOUT_SECONDS` | `120` | Bound a hung image pull. |

Azurite trails the newest storage service API version that the Azure SDK sends, and rejects otherwise
valid requests with `400 InvalidHeaderValue` when it does. The container is therefore started with
`--skipApiVersionCheck` by default. Setting `AIS_TEST_AZURITE_SKIP_API_VERSION_CHECK=false` re-enables
the check, which is how you deliberately confirm whether a skew exists between the SDK version in
`Directory.Packages.props` and the Azurite image in use:

```bash
AIS_TEST_AZURITE_SKIP_API_VERSION_CHECK=false dotnet run -f net10.0 -- --filter "TestCategory=Integration"
```

## Telemetry

The receiver is instrumented with OpenTelemetry — traces for the connection lifecycle, message
processing and storage operations; metrics for throughput, errors, connection health and storage
performance; and logs that carry trace context so a log line can be pivoted to the trace it belongs to.

`docs/telemetry/observability-playbook.md` is the operational reference: every metric with its unit and
histogram buckets, example queries, alert definitions, and troubleshooting runbooks. It records measured
baselines rather than estimates, including the ~3–4% sentence error rate a live coastal feed produces
(the parser rejects `!B1VDM`/`!B2VDM` talker ids), so alert thresholds can be set above a feed's own
noise floor rather than permanently firing. `docs/telemetry/testing-guide.md` covers testing
instrumented code.

### Exporting

Nothing is exported unless an endpoint is configured. Set `OTEL_EXPORTER_OTLP_ENDPOINT` to enable the
OTLP exporters for logs, metrics and traces; without it the exporters are left unregistered rather than
defaulting to `localhost:4317` and logging periodic connection failures into a deployment that has no
collector.

| Setting | Purpose |
| --- | --- |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Enables the OTLP exporters. Unset means no export. |
| `OTEL_TRACES_SAMPLER`, `OTEL_TRACES_SAMPLER_ARG` | Standard sampler selection. **Takes precedence** — when set, no sampler is configured in code, so this is the knob to reach for during an ingest-cost or backend-overload incident. |
| `OpenTelemetry:TraceSamplingRatio` | Ratio (0.0–1.0) used when the environment variables above are not set. Applied `ParentBased`, so a sampled upstream trace stays intact end to end. Ignored in Development, which always samples everything for the Aspire dashboard. |
| `OTEL_SERVICE_INSTANCE_ID` | Sets `service.instance.id`. Defaults to the machine name, which under Docker or Kubernetes is unique per replica and stable for that replica's life. |

### Health

Two health checks are registered: AIS connection state and storage accessibility. Connection health
reflects the live TCP state and, importantly, whether the feed is actually *delivering* — a server that
accepts a connection and then goes silent reconnects cleanly forever, so connect success alone is not
treated as healthy. `ais.receiver.connection.consecutive_failures` resets on the first delivered
sentence rather than on connect.

# Raspberry Pi

You have two options for running the AIS Receiver on a Raspberry Pi: using a Docker container or as a systemd service.

## Installation

The combination of Windows Terminal, .NET and PowerShell make a Raspberry Pi a very productive environment for .NET Devs.

Install [Windows Terminal](https://github.com/microsoft/terminal). You can download Windows Terminal from the [Microsoft Store](https://www.microsoft.com/en-gb/p/windows-terminal/9n0dx20hk701) or from the [GitHub releases page](https://github.com/microsoft/terminal/releases).

Open Windows Terminal and use `ssh pi@<Raspberry PI IP Address>` to connect to your Pi.

### Using Docker

These steps assume that you have [configured passwordless authentication on your Raspberry Pi](https://endjin.com/blog/2019/09/passwordless-ssh-from-windows-10-to-raspberry-pi).

Set up the required environment variables on your Raspberry Pi:

```bash
ssh user@pi
nano ~/.bashrc
```

Add the following lines to the end of the file:

```bash
export AIS_NET_RECEIVER_AZURE_CONNECTION_STRING="<YOUR_CONNECTION_STRING>"
```

Save & Exit nano. To load the environment variables, then run:

```bash
source ~/.bashrc
```

Install Docker & Docker Composer on your Raspberry Pi:

```bash
curl -sSL https://get.docker.com | sh
sudo usermod -aG docker $USER
sudo reboot
sudo apt install docker-compose
mkdir aisr
sudo apt-get update && sudo apt-get upgrade && sudo apt autoremove
exit
```

On your host machine, open Windows Terminal:

```bash
cd ./Solutions/Ais.Net.Receiver.Host.Console.RaspberryPi
scp .\docker-compose.yml user@pi:~/aisr/
ssh user@pi
cd aisr
docker-compose up -d
```

This will automatically pull the latest [image from Docker Hub](https://hub.docker.com/r/endjin/ais-dotnet-receiver) and run the AIS Receiver using the Azure Storage Connection String you configured as an environment variable. Use [Azure Storage Explorer](https://azure.microsoft.com/en-us/features/storage-explorer/) to browse to where files are captured. You should see entries added within the first minute of the service starting.

### Install as a systemd service

If you want to run the service as a daemon, you can use SystemD to manage the service.

#### Install .NET

Use the following commands to install .NET on your Pi.

1. `curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel Current`
1. `echo 'export DOTNET_ROOT=$HOME/.dotnet' >> ~/.bashrc`
1. `echo 'export PATH=$PATH:$HOME/.dotnet' >> ~/.bashrc`
1. `source ~/.bashrc`
1. `dotnet --version`

#### Install PowerShell 7.x

Use the following commands to install PowerShell on your Pi.

1. Download the latest package `wget https://github.com/PowerShell/PowerShell/releases/download/v7.5.0/powershell-7.5.0-linux-arm64.tar.gz`
1. Create a directory for it to be unpacked into `mkdir ~/powershell`
1. Unpack `tar -xvf ./powershell-7.5.0-linux-arm64.tar.gz -C ~/powershell`
1. Give it executable rights `sudo chmod +x /opt/microsoft/powershell/7/pwsh`
1. Create a symbolic link `sudo ln -s /opt/microsoft/powershell/7/pwsh /usr/bin/pwsh`

use the command `pwsh` to enter the PowerShell session.

#### Install Ais.Net.Receiver.Host.Console

2. From the solution root, open a command prompt and type `dotnet publish -c Release .\Solutions\Ais.Net.Receiver.slnx`
3. Add your Azure Blob Storage Account connection string to `appsettings.json`
4. Transfer (I use [Beyond Compare](https://www.scootersoftware.com/) as it has native SSH support) the contents of `.\Solutions\Ais.Net.Receiver.Host.Console\bin\Release\net10.0\publish` to a folder called `aisr` in the `home/pi` directory on your Raspberry Pi (assuming you still have the default set up.) 
5. Copy `Solutions\Ais.Net.Receiver.Host.Console.RaspberryPi\aisr.service` to `/lib/systemd/system/aisr.service`
6. run `sudo chmod 644 /lib/systemd/system/aisr.service`
7. run `sudo systemctl enable aisr.service`
8. run `sudo reboot`

You can use `journalctl -u "aisr"` to view the console output of `Ais.Net.Receiver.Host.Console.dll` 

You can use `sudo systemctl restart aisr` to restart the service.

If you need to look at / edit the deployed `aisr.service` use `sudo nano  /lib/systemd/system/aisr.service` make your edits then use `Ctrl+O` and `Ctrl+X` to save the file and exit.

Use [Azure Storage Explorer](https://azure.microsoft.com/en-us/features/storage-explorer/) to browse to where files are captured.

#### Configuration

Configuration is read from `appsettings.json`. Both hosts use the standard .NET host configuration
pipeline, so values can be overridden — in increasing order of precedence — by
`appsettings.{Environment}.json` (selected by `DOTNET_ENVIRONMENT`, e.g. `appsettings.Development.json`),
environment variables, and command-line arguments.

Environment variables use `__` as the section separator, which is the most practical way to override
settings in a container without rebuilding the image:

```bash
Ais__Connection__Host=153.44.253.27 \
Storage__EnableCapture=true \
Storage__ConnectionString="UseDevelopmentStorage=true" \
  ./Ais.Net.Receiver.Host.Worker
```

```json
{
  "Ais": {
    "Connection": {
      "Host": "153.44.253.27",
      "Port": 5631,
      "Retry": {
        "Attempts": 5,
        "Periodicity": "00:00:00:01"
      }
    },
    "Receiver": {
      "Retry": {
        "Attempts": 5,
        "Periodicity": "00:00:00:01"
      }
    },
    "Telemetry": {
      "Verbosity": "Warning",
      "StatisticsPeriodicity": "00:00:01:00",
      "VesselInactivityTimeout": "00:30:00"
    }
  },
  "Storage": {
    "enableCapture": true,
    "connectionString": "DefaultEndpointsProtocol=https;AccountName=<ACCOUNT_NAME>;AccountKey=<ACCOUNT_KEY>",
    "containerName": "nmea-ais-dev",
    "writeBatchSize": 500,
    "batchTimeoutSeconds": 10,
    "boundedCapacity": 10000,
    "maxDegreeOfParallelism": 1,
    "writeRetryAttempts": 3
  }
}
```

##### AIS

`Ais:Connection` controls the TCP connection to the feed.

- `Host`: IP address or FQDN of the AIS source
- `Port`: port number for the AIS source
- `Retry:Periodicity`: base delay between reconnection attempts. Backoff is linear —
  `Periodicity × attempt`.
- `Retry:Attempts`: **caps how far the backoff delay grows, and does not limit the number of
  attempts.** The receiver reconnects indefinitely; with the shipped `Attempts: 5` and
  `Periodicity: 1s` the delay climbs to 5 seconds and then stays there. A long-running receiver that
  gave up after N failures would need manual intervention to resume, which is why there is no cap on
  attempts. The idle-read timeout that detects a silently dropped connection is deliberately separate
  from these settings and defaults to 60 seconds.

`Ais:Receiver:Retry` controls the receive loop that sits above the connection, using the same shape.

`Ais:Telemetry` controls console output and derived streams.

- `Verbosity`: a standard `LogLevel` (`Trace`, `Debug`, `Information`, `Warning`, `Error`,
  `Critical`, `None`), and **cumulative** — each level includes everything the less verbose levels
  emit:
  - `None` — nothing
  - `Warning` — periodic statistics, sampled at `StatisticsPeriodicity`
  - `Information` — the above, plus vessel names and positions
  - `Debug` — the above, plus raw NMEA sentences
  - `Trace` — the above, plus decoded messages and parse errors
- `StatisticsPeriodicity`: how often to emit the statistics line
- `VesselInactivityTimeout`: how long a vessel may go unheard before its tracking state is released.
  Defaults to 30 minutes.

##### Storage

These settings control the capturing of NMEA sentences to Azure Blob Storage.

- `enableCapture`: whether to capture NMEA sentences and write them to Azure Blob Storage
- `connectionString`: Azure Storage account connection string
- `containerName`: container to capture the NMEA sentences into. Useful for separating a local dev
  container from production within the same storage account.
- `writeBatchSize`: how many sentences to batch before writing (default 500)
- `batchTimeoutSeconds`: flush a partial batch after this long, so a quiet feed still gets persisted
  (default 10)
- `boundedCapacity`: how many sentences may be buffered awaiting a write (default 10000). When the
  buffer fills, sentences are shed rather than allowed to grow memory without limit, and the drops are
  counted by `ais.receiver.sentences.dropped`.
- `maxDegreeOfParallelism`: concurrent storage writes (default 1). Appends to a single blob are
  serialized regardless, so raising this only helps if writes are the bottleneck.
- `writeRetryAttempts`: total attempts per batch, including the first (default 3)
- `deadLetterPath`: optional local directory. When set, a batch that exhausts its retries is written
  here instead of being lost, and a background replayer returns it to storage once the backend
  recovers. When unset, an exhausted batch surfaces as an error instead.
- `deadLetterReplayIntervalSeconds`: how often to attempt replaying dead-lettered batches (default 60)

## Running as WASM

A [Proof of Concept](https://github.com/endjin/componentize-dotnet-demo?tab=readme-ov-file#aisnetreceiverhostwasi-demo) using [componentize-dotnet](https://github.com/bytecodealliance/componentize-dotnet) and a custom [WASI](https://github.com/WebAssembly/WASI) implementation of `WasiSocketNmeaStreamReader` enables the receiver to run as WASM via Wasmtime.

## Licenses

[![GitHub license](https://img.shields.io/badge/license-Apache%202-blue.svg)](https://raw.githubusercontent.com/ais-dotnet/Ais.Net.Receiver/master/LICENSE)

AIS.Net.Receiver is also available under the Apache 2.0 open source license.
 
The Data ingested by the AIS.Net.Receiver is licensed under the [Norwegian license for public data (NLOD)](https://data.norge.no/nlod/en/2.0).

## Project Sponsor

This project is sponsored by [endjin](https://endjin.com), a UK based Technology Consultancy which specializes in Data & Analytics, AI & Cloud Native App Dev, and is a [.NET Foundation Corporate Sponsor](https://dotnetfoundation.org/membership/corporate-sponsorship).

> We help small teams achieve big things.

We produce two free weekly newsletters: 

 - [Azure Weekly](https://azureweekly.info) for all things about the Microsoft Azure Platform
 - [Power BI Weekly](https://powerbiweekly.info) for all things Power BI, Microsoft Fabric, and Azure Synapse Analytics

Keep up with everything that's going on at endjin via our [blog](https://endjin.com/blog), follow us on [Twitter](https://twitter.com/endjin), [YouTube](https://www.youtube.com/c/endjin) or [LinkedIn](https://www.linkedin.com/company/endjin).

We have become the maintainers of a number of popular .NET Open Source Projects:

- [Reactive Extensions for .NET](https://github.com/dotnet/reactive)
- [Reaqtor](https://github.com/reaqtive)
- [Argotic Syndication Framework](https://github.com/argotic-syndication-framework/)

And we have over 50 Open Source projects of our own, spread across the following GitHub Orgs:

- [endjin](https://github.com/endjin/)
- [Corvus](https://github.com/corvus-dotnet)
- [Menes](https://github.com/menes-dotnet)
- [Marain](https://github.com/marain-dotnet)
- [AIS.NET](https://github.com/ais-dotnet)

And the DevOps tooling we have created for managing all these projects is available on the [PowerShell Gallery](https://www.powershellgallery.com/profiles/endjin).

For more information about our products and services, or for commercial support of this project, please [contact us](https://endjin.com/contact-us). 

## Code of conduct

This project has adopted a code of conduct adapted from the [Contributor Covenant](http://contributor-covenant.org/) to clarify expected behaviour in our community. This code of conduct has been [adopted by many other projects](http://contributor-covenant.org/adopters/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [&#104;&#101;&#108;&#108;&#111;&#064;&#101;&#110;&#100;&#106;&#105;&#110;&#046;&#099;&#111;&#109;](&#109;&#097;&#105;&#108;&#116;&#111;:&#104;&#101;&#108;&#108;&#111;&#064;&#101;&#110;&#100;&#106;&#105;&#110;&#046;&#099;&#111;&#109;) with any additional questions or comments.

## IP Maturity Model (IMM)

[![Shared Engineering Standards](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/74e29f9b-6dca-4161-8fdd-b468a1eb185d?nocache=true)](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/74e29f9b-6dca-4161-8fdd-b468a1eb185d?cache=false)

[![Coding Standards](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/f6f6490f-9493-4dc3-a674-15584fa951d8?cache=false)](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/f6f6490f-9493-4dc3-a674-15584fa951d8?cache=false)

[![Executable Specifications](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/bb49fb94-6ab5-40c3-a6da-dfd2e9bc4b00?cache=false)](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/bb49fb94-6ab5-40c3-a6da-dfd2e9bc4b00?cache=false)

[![Code Coverage](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/0449cadc-0078-4094-b019-520d75cc6cbb?cache=false)](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/0449cadc-0078-4094-b019-520d75cc6cbb?cache=false)

[![Benchmarks](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/64ed80dc-d354-45a9-9a56-c32437306afa?cache=false)](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/64ed80dc-d354-45a9-9a56-c32437306afa?cache=false)

[![Reference Documentation](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/2a7fc206-d578-41b0-85f6-a28b6b0fec5f?cache=false)](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/2a7fc206-d578-41b0-85f6-a28b6b0fec5f?cache=false)

[![Design & Implementation Documentation](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/f026d5a2-ce1a-4e04-af15-5a35792b164b?cache=false)](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/f026d5a2-ce1a-4e04-af15-5a35792b164b?cache=false)

[![How-to Documentation](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/145f2e3d-bb05-4ced-989b-7fb218fc6705?cache=false)](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/145f2e3d-bb05-4ced-989b-7fb218fc6705?cache=false)

[![Date of Last IP Review](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/da4ed776-0365-4d8a-a297-c4e91a14d646?cache=false)](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/da4ed776-0365-4d8a-a297-c4e91a14d646?cache=false)

[![Framework Version](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/6c0402b3-f0e3-4bd7-83fe-04bb6dca7924?cache=false)](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/6c0402b3-f0e3-4bd7-83fe-04bb6dca7924?cache=false)

[![Associated Work Items](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/79b8ff50-7378-4f29-b07c-bcd80746bfd4?cache=false)](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/79b8ff50-7378-4f29-b07c-bcd80746bfd4?cache=false)

[![Source Code Availability](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/30e1b40b-b27d-4631-b38d-3172426593ca?cache=false)](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/30e1b40b-b27d-4631-b38d-3172426593ca?cache=false)

[![License](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/d96b5bdc-62c7-47b6-bcc4-de31127c08b7?cache=false)](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/d96b5bdc-62c7-47b6-bcc4-de31127c08b7?cache=false)

[![Production Use](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/87ee2c3e-b17a-4939-b969-2c9c034d05d7?cache=false)](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/87ee2c3e-b17a-4939-b969-2c9c034d05d7?cache=false)

[![Insights](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/71a02488-2dc9-4d25-94fa-8c2346169f8b?cache=false)](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/71a02488-2dc9-4d25-94fa-8c2346169f8b?cache=false)

[![Packaging](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/547fd9f5-9caf-449f-82d9-4fba9e7ce13a?cache=false)](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/547fd9f5-9caf-449f-82d9-4fba9e7ce13a?cache=false)

[![Deployment](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/edea4593-d2dd-485b-bc1b-aaaf18f098f9?cache=false)](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/edea4593-d2dd-485b-bc1b-aaaf18f098f9?cache=false)

[![OpenChain](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/66efac1a-662c-40cf-b4ec-8b34c29e9fd7?cache=false)](https://imm.endjin.com/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/66efac1a-662c-40cf-b4ec-8b34c29e9fd7?cache=false)
