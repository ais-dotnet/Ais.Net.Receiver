import {connect, StringCodec} from 'nats.ws';
import {TRAIL_LENGTH_SECONDS} from '../config.js';
import {isPositionReport, readName, readPosition, readShipType} from './ais-messages.js';

const DEFAULT_COLOR = [150, 249, 161];

/**
 * Subscribes to the receiver's decoded AIS messages straight from NATS over a websocket, and builds
 * vessels from them in the same shape the replay path produces, so every deck.gl layer works
 * unchanged for both modes.
 *
 * Correlation happens here because AIS splits a vessel across messages: position reports carry no
 * name, and the name and ship type arrive on static messages every few minutes. A vessel therefore
 * appears as soon as it reports a position, under a placeholder name, and gains its real name and
 * colour when its static message turns up - rather than staying invisible until then.
 *
 * Positions carry a timestamp in seconds relative to the moment the page connected, mirroring how the
 * pipeline rebases a recording's epochs. That is what lets TripsLayer's trail and the interpolation in
 * data-loader.js behave identically whether the data is live or recorded.
 */
export class LiveVesselSource {
  constructor({url, subject, user, pass, shipTypeStyles, inactivitySeconds = 900}) {
    this.url = url;
    this.subject = subject;
    this.user = user;
    this.pass = pass;
    this.shipTypeStyles = shipTypeStyles ?? {};
    this.inactivitySeconds = inactivitySeconds;

    this.baseEpoch = Math.floor(Date.now() / 1000);
    this.vessels = new Map();
    this.connection = null;
  }

  /** Seconds since this source started, the time base the layers render against. */
  get currentTime() {
    return Math.floor(Date.now() / 1000) - this.baseEpoch;
  }

  get metadata() {
    return {
      baseEpoch: this.baseEpoch,
      timeRange: {start: 0, end: this.currentTime},
      vesselCount: this.vessels.size
    };
  }

  /** The vessels, as an array the layers can bind to directly. */
  get vesselList() {
    return [...this.vessels.values()];
  }

  async start(onError) {
    this.connection = await connect({
      servers: this.url,
      user: this.user,
      pass: this.pass,

      // A demo left open overnight should recover from a broker restart rather than silently stop.
      maxReconnectAttempts: -1,
      reconnectTimeWait: 2000
    });

    const codec = StringCodec();
    const subscription = this.connection.subscribe(this.subject);

    (async () => {
      for await (const message of subscription) {
        try {
          this.#apply(JSON.parse(codec.decode(message.data)));
        } catch (err) {
          // One malformed message must not end the subscription and freeze the map.
          console.warn('Dropped an unreadable AIS message', err);
        }
      }
    })().catch(onError);

    return this;
  }

  async stop() {
    if (this.connection) {
      await this.connection.close();
      this.connection = null;
    }
  }

  #vesselFor(mmsi) {
    let vessel = this.vessels.get(mmsi);

    if (!vessel) {
      vessel = {
        mmsi,
        name: `MMSI ${mmsi}`,
        shipType: '',
        shipTypeCategory: '',
        color: DEFAULT_COLOR,
        path: [],
        timestamps: [],
        positions: []
      };
      this.vessels.set(mmsi, vessel);
    }

    return vessel;
  }

  #apply(message) {
    const mmsi = message.Mmsi;
    if (mmsi == null) return;

    const name = readName(message);
    const shipType = readShipType(message);

    if (name || shipType !== null) {
      const vessel = this.#vesselFor(mmsi);

      if (name) {
        vessel.name = name;
      }

      if (shipType !== null) {
        const style = this.shipTypeStyles[shipType];
        vessel.shipType = String(shipType);
        vessel.shipTypeCategory = style?.category ?? '';
        vessel.color = style?.color ?? DEFAULT_COLOR;
      }
    }

    if (!isPositionReport(message)) return;

    const position = readPosition(message);
    if (!position) return;

    const vessel = this.#vesselFor(mmsi);
    const timestamp = this.currentTime;

    vessel.path.push(position.coordinates);
    vessel.timestamps.push(timestamp);
    vessel.positions.push({
      coordinates: position.coordinates,
      timestamp,
      speed: position.speed,
      course: position.course
    });

    this.#trim(vessel, timestamp);
  }

  /**
   * Drops points older than the visible trail. Without this a long-running page accumulates every
   * position of every vessel, and both memory and the per-frame layer cost grow without limit.
   */
  #trim(vessel, now) {
    const cutoff = now - TRAIL_LENGTH_SECONDS;
    let drop = 0;
    while (drop < vessel.positions.length - 1 && vessel.positions[drop].timestamp < cutoff) drop++;

    if (drop > 0) {
      vessel.positions.splice(0, drop);
      vessel.path.splice(0, drop);
      vessel.timestamps.splice(0, drop);
    }
  }

  /** Forgets vessels that have gone quiet, so the map reflects what is actually out there now. */
  prune() {
    const cutoff = this.currentTime - this.inactivitySeconds;

    for (const [mmsi, vessel] of this.vessels) {
      const last = vessel.positions.at(-1);

      // A vessel seen only through a static message has no positions yet; keep it, since its
      // position report may still arrive.
      if (last && last.timestamp < cutoff) {
        this.vessels.delete(mmsi);
      }
    }
  }
}
