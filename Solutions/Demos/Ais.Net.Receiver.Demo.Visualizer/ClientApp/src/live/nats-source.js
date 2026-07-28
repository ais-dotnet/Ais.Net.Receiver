import {connect, StringCodec} from 'nats.ws';
import {TRAIL_LENGTH_SECONDS} from '../config.js';

/**
 * Subscribes to live vessel positions straight from NATS over a websocket and maintains them in the
 * same shape the replay path produces, so every deck.gl layer works unchanged for both modes.
 *
 * Positions carry a timestamp in seconds relative to the moment the page connected, mirroring how the
 * pipeline rebases a recording's epochs. That is what lets TripsLayer's trail and the interpolation in
 * data-loader.js behave identically whether the data is live or recorded.
 */
export class LiveVesselSource {
  constructor({url, subject, user, pass, inactivitySeconds = 900}) {
    this.url = url;
    this.subject = subject;
    this.user = user;
    this.pass = pass;
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
          console.warn('Dropped an unreadable vessel update', err);
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

  #apply(update) {
    const timestamp = update.timestamp - this.baseEpoch;
    let vessel = this.vessels.get(update.mmsi);

    if (!vessel) {
      vessel = {
        mmsi: update.mmsi,
        name: update.name,
        shipType: update.shipType,
        shipTypeCategory: update.shipTypeCategory,
        color: update.color,
        path: [],
        timestamps: [],
        positions: []
      };
      this.vessels.set(update.mmsi, vessel);
    } else {
      // Identity arrives on separate, less frequent messages, so it can improve after first sighting.
      vessel.name = update.name;
      vessel.shipType = update.shipType;
      vessel.shipTypeCategory = update.shipTypeCategory;
      vessel.color = update.color;
    }

    vessel.path.push(update.coordinates);
    vessel.timestamps.push(timestamp);
    vessel.positions.push({
      coordinates: update.coordinates,
      timestamp,
      speed: update.speed,
      course: update.course
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
      if (!last || last.timestamp < cutoff) {
        this.vessels.delete(mmsi);
      }
    }
  }
}
