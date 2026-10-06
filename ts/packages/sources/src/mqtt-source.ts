// Mori.SkyScope — MQTT source over WebSocket: subscribes to topic filters and maps JSON payloads to samples.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { mapMqttMessage, mqttChannels, samplesToFrame, type MqttRule, type MqttSample, type Source, type SourceContext, type SourceFactory } from "@mori/skyscope-core";
import mqtt, { type MqttClient } from "mqtt";

/** Configuration of `MqttSource`. */
export interface MqttSourceConfig {
  /** Broker URL — in browsers this must be a WebSocket endpoint (`ws://host:9001/mqtt`). */
  url: string;
  /** Topic filters and how their JSON payloads map to channels; one subscription per distinct filter. */
  rules: MqttRule[];
  /** Optional broker credentials and client id, passed through to the MQTT client. */
  username?: string | undefined; password?: string | undefined; clientId?: string | undefined;
  /** Samples are batched into one frame every `batchMs` (default 50). */
  batchMs?: number | undefined;
  /** Reconnect period in milliseconds after a drop (default 2000). */
  reconnectMs?: number | undefined;
}

/** MQTT source: subscribes to every rule's topic filter and turns payloads into timestamped frames through `mapMqttMessage`. */
export class MqttSource implements Source {
  /** Registry type id of this source. */
  readonly type = "mqtt";
  private client: MqttClient | null = null;
  private ctx: SourceContext | null = null;
  private rules: MqttRule[] = [];
  private pending: MqttSample[] = [];
  private timer: ReturnType<typeof setInterval> | null = null;
  private seq = 0;
  /** True between the broker's connect acknowledgement and the next close. */
  connected = false;
  /** Count of MQTT messages received since `start`, matched or not. */
  messagesReceived = 0;

  /** Declares the rules' channels, connects to the broker and starts the batching timer. Samples are stamped with `ctx.clock` on arrival. */
  start(ctx: SourceContext, config: MqttSourceConfig): void {
    this.ctx = ctx; this.rules = config.rules;
    for (const c of mqttChannels(config.rules)) ctx.signals.declareChannel(c);
    const opts: mqtt.IClientOptions = { reconnectPeriod: config.reconnectMs ?? 2000 };
    if (config.username !== undefined) opts.username = config.username;
    if (config.password !== undefined) opts.password = config.password;
    if (config.clientId !== undefined) opts.clientId = config.clientId;
    const client = mqtt.connect(config.url, opts);
    this.client = client;
    client.on("connect", () => {
      this.connected = true;
      const topics = [...new Set(config.rules.map((r) => r.topic))];
      client.subscribe(topics, (err) => { if (err) ctx.log("error", `mqtt: subscribe failed: ${err.message}`); else ctx.log("info", `mqtt: subscribed to ${topics.length} filters`); });
    });
    client.on("close", () => { this.connected = false; });
    client.on("error", (e) => ctx.log("warn", `mqtt: ${e.message}`));
    client.on("message", (topic, payload) => {
      this.messagesReceived++;
      const samples = mapMqttMessage(this.rules, topic, payload.toString("utf8"), ctx.clock.now());
      for (const s of samples) this.pending.push(s);
    });
    this.timer = setInterval(() => this.flush(), config.batchMs ?? 50);
  }

  private flush(): void {
    if (this.pending.length === 0 || !this.ctx) return;
    const frame = samplesToFrame(this.seq++, this.pending);
    this.pending = [];
    if (frame) this.ctx.signals.pushFrame(frame);
  }

  /** Flushes the pending samples as a last frame, then force-closes the client. */
  stop(): void {
    if (this.timer) clearInterval(this.timer); this.timer = null;
    this.flush();
    this.client?.end(true); this.client = null; this.connected = false; this.ctx = null;
  }
}

/** Registry entry for `MqttSource`. */
export const mqttFactory: SourceFactory = { type: "mqtt", displayName: "MQTT (over WebSocket)", capabilities: ["signals"], create: () => new MqttSource() };
