# Ais.Net.Receiver

[![Build Status](https://dev.azure.com/endjin-labs/Ais.Net.Receiver/_apis/build/status/ais-dotnet.Ais.Net.Receiver?branchName=master)](https://dev.azure.com/endjin-labs/Ais.Net.Receiver/_build/latest?definitionId=1&branchName=master)

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

The commercial AIS.Net.Receiver license gives you the full rights to create and distribute software on your own terms without any open source license obligations. With the commercial license you also have access to official AIS.Net.Receiver support and close strategic relationship with endjin to make sure your development goals are met.

AIS.Net.Receiver is also available under the Microsoft Public License (MS-PL) open source license. The AIS.Net.Receiver open source license is ideal for use cases such as open source projects with open source distribution, student/academic purposes, hobby projects, internal research projects without external distribution, or other projects where all Microsoft Public License obligations can be met. 

For any licensing questions, please email [&#108;&#105;&#99;&#101;&#110;&#115;&#105;&#110;&#103;&#64;&#101;&#110;&#100;&#106;&#105;&#110;&#46;&#99;&#111;&#109;](&#109;&#97;&#105;&#108;&#116;&#111;&#58;&#108;&#105;&#99;&#101;&#110;&#115;&#105;&#110;&#103;&#64;&#101;&#110;&#100;&#106;&#105;&#110;&#46;&#99;&#111;&#109;)
 
The Data ingested by the AIS.Net.Receiver is licensed under the [Norwegian license for public data (NLOD)](https://data.norge.no/nlod/en/2.0).

## Project Sponsor

This project is sponsored by [endjin](https://endjin.com), a UK based Microsoft Gold Partner for Cloud Platform, Data Platform, Data Analytics, DevOps, and a Power BI Partner.

For more information about our products and services, or for commercial support of this project, please [contact us](https://endjin.com/contact-us). 

We produce two free weekly newsletters; [Azure Weekly](https://azureweekly.info) for all things about the Microsoft Azure Platform, and [Power BI Weekly](https://powerbiweekly.info).

Keep up with everything that's going on at endjin via our [blog](https://blogs.endjin.com/), follow us on [Twitter](https://twitter.com/endjin), or [LinkedIn](https://www.linkedin.com/company/1671851/).

Our other Open Source projects can be found on [GitHub](https://github.com/endjin)

## Code of conduct

This project has adopted a code of conduct adapted from the [Contributor Covenant](http://contributor-covenant.org/) to clarify expected behavior in our community. This code of conduct has been [adopted by many other projects](http://contributor-covenant.org/adopters/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [&#104;&#101;&#108;&#108;&#111;&#064;&#101;&#110;&#100;&#106;&#105;&#110;&#046;&#099;&#111;&#109;](&#109;&#097;&#105;&#108;&#116;&#111;:&#104;&#101;&#108;&#108;&#111;&#064;&#101;&#110;&#100;&#106;&#105;&#110;&#046;&#099;&#111;&#109;) with any additional questions or comments.