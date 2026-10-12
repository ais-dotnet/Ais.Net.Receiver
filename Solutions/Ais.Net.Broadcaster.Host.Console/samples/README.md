# Sample recordings

Both files are NMEA 4.10 recordings with CRLF line endings, so that `ais-broadcaster replay` has
something to broadcast out of the box. The sentences originate from the Norwegian Coastal
Administration's open AIS feed and are used under the Norwegian Licence for Open Government Data
(NLOD) 2.0: "Contains data under the Norwegian licence for Open Government data (NLOD) distributed
by the Norwegian Coastal Administration".

## `kystverket-20260511T00.nm4`

The first 2,000 lines of a capture the receiver wrote on 2026-05-11, exactly as the feed sent them:
about 53 seconds of traffic from a few dozen base stations, with the feed's real tag blocks
(`\s:<station>,c:<unix seconds>*<checksum>\`), `!BSVDM` sentences, and 327 multi-part pairs kept
whole. Because each base station stamps its own clock, timestamps occasionally step back by a
second; tag-block pacing treats a backward step as no delay. Sentences carry the talker ids `BS`, `B1` and `B2`, as the feed
sends them. The Aspire AppHost replays this file when it is present (it is not committed by default;

```
ais-broadcaster replay samples/kystverket-20260511T00.nm4 --tcp 5631 --ws 8080
```

## `sample.nm4`

Sixteen sentences, 13 seconds of traffic, assembled from the receiver's own test data: two
multi-part type 5 reports kept in order, and a mix of position reports. The tag blocks are
synthetic (`\s:2573010,c:<unix seconds>*<checksum>\`), with timestamps that give a replay steady
one-second traffic, a burst and a four-second lull. It is small enough to read, which is its point.
