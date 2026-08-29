# LCU Traffic

## Overview

The LCU Traffic page records LCU and Riot client traffic for debugging and endpoint discovery. It is available from the main navigation and keeps the newest 1,000 records in memory.

## Captured traffic

- REST requests and responses made by League Account Manager through its shared LCU connector, combined into one item per request.
- Full incoming and outgoing frames for the tracker's League LCU WebSocket connection, including direction, opcode envelope, URI, event type, and event payload.
- HTTP traffic captured by the debug client launcher, including request and response headers, bodies, status, and duration.
- XMPP, RMS, and RTMP proxy traffic, including direction and decoded payloads where available.
- Method, endpoint/query, request headers, status, duration, request body, response headers, and response body where available.

LCU REST requests made by the application are captured directly by the shared connector. The League Client's own local LCU REST calls are not transparently intercepted; the debug launcher proxies configured external service origins and the supported XMPP, RMS, and RTMP services. LCU WebSocket events are monitored through the client's local WebSocket endpoint.

Captured headers and payloads can contain authorization tokens, cookies, credentials, or other sensitive values. Treat exported JSON and debug logs as confidential.

## Controls

- Search by endpoint, method, request body, response body, request headers, or response headers.
- Filter by REST, HTTP, WebSocket, XMPP, RMS, or RTMP traffic.
- Select individual rows with the Export checkbox.
- Select all currently visible rows.
- Export selected rows to formatted JSON.
- Load a selected request into the LCU request composer or send a custom LCU request.
- Clear all captured traffic.