# Ais.Net.Receiver

[![Build Status](https://dev.azure.com/endjin-labs/Ais.Net.Receiver/_apis/build/status/ais-dotnet.Ais.Net.Receiver?branchName=master)](https://dev.azure.com/endjin-labs/Ais.Net.Receiver/_build/latest?definitionId=1&branchName=master)

A simple .NET AIS Receiver for capturing the Norwegian Coastal Administration's marine Automatic Identification System (AIS) [AIVDM/AIVDO](https://gpsd.gitlab.io/gpsd/AIVDM.html) NMEA message network data (available under [Norwegian license for public data (NLOD)](https://data.norge.no/nlod/en/2.0)) and persisting in Microsoft Azure Blob Storage.

The [Norwegian Costal Administration provide a TCP endpoint](https://ais.kystverket.no/) (`153.44.253.27:5631`) for broadcasting their raw AIS AIVDM/AIVDO sentences, captured by over 50 base stations, and covers the area 40-60 nautical miles from the Norwegian coastline.

This project contains a .NET Core console application that will reliably ingest the TCP stream, batch the AIVDM/AIVDO sentences and write them to Azure Blob Storage using the [Append Blob](https://docs.microsoft.com/en-us/rest/api/storageservices/append-block) feature, to create timestamped hour-long rolling logs.

The purpose of this application is to provide sample data for [Ais.Net](https://github.com/ais-dotnet/Ais.Net) - the .NET Standard, high performance, zero allocation AIS decoder. The majority of raw AIS data is only available via commerical sources, and thus creating AIS datasets large enough to test / benchmark [Ais.Net](https://github.com/ais-dotnet/Ais.Net) is almost impossible. 

The Norwegian Costal Administration TCP endpoint produces: 
- ~2.2 KB per second 
- ~8MB per hour 
- ~190MB per day 
- ~1.3 GB per week 
- ~5.6 GB per month
- ~67 GB per year

## To Run

From the command line: `dotnet Ais.Net.Receiver.dll`

## Licenses

The code for the AIS .NET Receiver is licensed under MIT. 
The Data ingested by the AIS .NET Receiver is licensed under the [Norwegian license for public data (NLOD)](https://data.norge.no/nlod/en/2.0).

## Project Sponsor

This project is sponsored by [endjin](https://endjin.com), a UK based Microsoft Gold Partner for Cloud Platform, Data Platform, Data Analytics, DevOps, and a Power BI Partner.

For more information about our products and services, or for commercial support of this project, please [contact us](https://endjin.com/contact-us). 

We produce two free weekly newsletters; [Azure Weekly](https://azureweekly.info) for all things about the Microsoft Azure Platform, and [Power BI Weekly](https://powerbiweekly.info).

Keep up with everything that's going on at endjin via our [blog](https://blogs.endjin.com/), follow us on [Twitter](https://twitter.com/endjin), or [LinkedIn](https://www.linkedin.com/company/1671851/).

Our other Open Source projects can be found on [GitHub](https://github.com/endjin)

## Code of conduct

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).  For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [&#104;&#101;&#108;&#108;&#111;&#064;&#101;&#110;&#100;&#106;&#105;&#110;&#046;&#099;&#111;&#109;](&#109;&#097;&#105;&#108;&#116;&#111;:&#104;&#101;&#108;&#108;&#111;&#064;&#101;&#110;&#100;&#106;&#105;&#110;&#046;&#099;&#111;&#109;) with any additional questions or comments.