# Ais.Net.Receiver

[![Build Status](https://dev.azure.com/endjin-labs/Ais.Net.Receiver/_apis/build/status/ais-dotnet.Ais.Net.Receiver?branchName=master)](https://dev.azure.com/endjin-labs/Ais.Net.Receiver/_build/latest?definitionId=1&branchName=master)

[![IMM](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/total?cache=false)](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/total?cache=false)

A simple .NET Core AIS Receiver for capturing the Norwegian Coastal Administration's marine Automatic Identification System (AIS) [AIVDM/AIVDO](https://gpsd.gitlab.io/gpsd/AIVDM.html) NMEA message network data (available under [Norwegian license for public data (NLOD)](https://data.norge.no/nlod/en/2.0)) and persisting in Microsoft Azure Blob Storage.

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

## Azure Blob Storage Taxonomy

The AIS data is stored using the following taxonomy

`<USER DEFINED CONTAINER NAME>/raw/yyyyMMdd/yyyyMMddTHH.nm4`

An example directory listing, with a user defined container name of `nmea-ais` would look as follows:

```
\---nmea-ais
    \---raw
        +---20190712
        |       20190712T00.nm4
        |       20190712T01.nm4
        |       20190712T02.nm4
        |       20190712T03.nm4
        |       20190712T04.nm4
        |       20190712T05.nm4
        |       20190712T06.nm4
        |       20190712T07.nm4
        |       20190712T08.nm4
        |       20190712T09.nm4
        |       20190712T10.nm4
        |       20190712T11.nm4
        |       20190712T12.nm4
        |       20190712T13.nm4
        |       20190712T14.nm4
        |       20190712T15.nm4
        |       20190712T16.nm4
        |       20190712T17.nm4
        |       20190712T18.nm4
        |       20190712T19.nm4
        |       20190712T20.nm4
        |       20190712T21.nm4
        |       20190712T22.nm4
        |       20190712T23.nm4
        |
        +---20190713
        |       20190713T00.nm4
        |       20190713T01.nm4
```

## To Run

Update the values in the `settings.json` file:

```
{
  "connectionString": "<YOUR AZURE STORAGE CONNECTION STRING>",
  "containerName": "<YOUR ROOT BLOB STORAGE CONTAINER NAME>"
} 
```

From the command line: `dotnet Ais.Net.Receiver.dll`

## Licenses

[![GitHub license](https://img.shields.io/badge/license-Apache%202-blue.svg)](https://raw.githubusercontent.com/ais-dotnet/Ais.Net.Receiver/master/LICENSE)

AIS.Net.Receiver is also available under the Apache 2.0 open source license.
 
The Data ingested by the AIS.Net.Receiver is licensed under the [Norwegian license for public data (NLOD)](https://data.norge.no/nlod/en/2.0).

## Project Sponsor

This project is sponsored by [endjin](https://endjin.com), a UK based Microsoft Gold Partner for Cloud Platform, Data Platform, Data Analytics, DevOps, and a Power BI Partner.

For more information about our products and services, or for commercial support of this project, please [contact us](https://endjin.com/contact-us). 

We produce two free weekly newsletters; [Azure Weekly](https://azureweekly.info) for all things about the Microsoft Azure Platform, and [Power BI Weekly](https://powerbiweekly.info).

Keep up with everything that's going on at endjin via our [blog](https://blogs.endjin.com/), follow us on [Twitter](https://twitter.com/endjin), or [LinkedIn](https://www.linkedin.com/company/1671851/).

Our other Open Source projects can be found on [GitHub](https://github.com/endjin)

## Code of conduct

This project has adopted a code of conduct adapted from the [Contributor Covenant](http://contributor-covenant.org/) to clarify expected behavior in our community. This code of conduct has been [adopted by many other projects](http://contributor-covenant.org/adopters/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [&#104;&#101;&#108;&#108;&#111;&#064;&#101;&#110;&#100;&#106;&#105;&#110;&#046;&#099;&#111;&#109;](&#109;&#097;&#105;&#108;&#116;&#111;:&#104;&#101;&#108;&#108;&#111;&#064;&#101;&#110;&#100;&#106;&#105;&#110;&#046;&#099;&#111;&#109;) with any additional questions or comments.

## IP Maturity Matrix (IMM)

The IMM is endjin's 

[![Shared Engineering Standards](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/74e29f9b-6dca-4161-8fdd-b468a1eb185d?nocache=true)](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/74e29f9b-6dca-4161-8fdd-b468a1eb185d?cache=false)

[![Coding Standards](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/f6f6490f-9493-4dc3-a674-15584fa951d8?cache=false)](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/f6f6490f-9493-4dc3-a674-15584fa951d8?cache=false)

[![Executable Specifications](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/bb49fb94-6ab5-40c3-a6da-dfd2e9bc4b00?cache=false)](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/bb49fb94-6ab5-40c3-a6da-dfd2e9bc4b00?cache=false)

[![Code Coverage](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/0449cadc-0078-4094-b019-520d75cc6cbb?cache=false)](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/0449cadc-0078-4094-b019-520d75cc6cbb?cache=false)

[![Benchmarks](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/64ed80dc-d354-45a9-9a56-c32437306afa?cache=false)](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/64ed80dc-d354-45a9-9a56-c32437306afa?cache=false)

[![Reference Documentation](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/2a7fc206-d578-41b0-85f6-a28b6b0fec5f?cache=false)](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/2a7fc206-d578-41b0-85f6-a28b6b0fec5f?cache=false)

[![Design & Implementation Documentation](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/f026d5a2-ce1a-4e04-af15-5a35792b164b?cache=false)](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/f026d5a2-ce1a-4e04-af15-5a35792b164b?cache=false)

[![How-to Documentation](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/145f2e3d-bb05-4ced-989b-7fb218fc6705?cache=false)](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/145f2e3d-bb05-4ced-989b-7fb218fc6705?cache=false)

[![Date of Last IP Review](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/da4ed776-0365-4d8a-a297-c4e91a14d646?cache=false)](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/da4ed776-0365-4d8a-a297-c4e91a14d646?cache=false)

[![Framework Version](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/6c0402b3-f0e3-4bd7-83fe-04bb6dca7924?cache=false)](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/6c0402b3-f0e3-4bd7-83fe-04bb6dca7924?cache=false)

[![Associated Work Items](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/79b8ff50-7378-4f29-b07c-bcd80746bfd4?cache=false)](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/79b8ff50-7378-4f29-b07c-bcd80746bfd4?cache=false)

[![Source Code Availability](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/30e1b40b-b27d-4631-b38d-3172426593ca?cache=false)](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/30e1b40b-b27d-4631-b38d-3172426593ca?cache=false)

[![License](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/d96b5bdc-62c7-47b6-bcc4-de31127c08b7?cache=false)](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/d96b5bdc-62c7-47b6-bcc4-de31127c08b7?cache=false)

[![Production Use](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/87ee2c3e-b17a-4939-b969-2c9c034d05d7?cache=false)](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/87ee2c3e-b17a-4939-b969-2c9c034d05d7?cache=false)

[![Insights](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/71a02488-2dc9-4d25-94fa-8c2346169f8b?cache=false)](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/71a02488-2dc9-4d25-94fa-8c2346169f8b?cache=false)

[![Packaging](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/547fd9f5-9caf-449f-82d9-4fba9e7ce13a?cache=false)](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/547fd9f5-9caf-449f-82d9-4fba9e7ce13a?cache=false)

[![Deployment](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/edea4593-d2dd-485b-bc1b-aaaf18f098f9?cache=false)](https://endimmfuncdev.azurewebsites.net/api/imm/github/ais-dotnet/Ais.Net.Receiver/rule/edea4593-d2dd-485b-bc1b-aaaf18f098f9?cache=false)