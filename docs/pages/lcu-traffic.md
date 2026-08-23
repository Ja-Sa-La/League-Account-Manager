# LCU Traffic

## Overview

The LCU Traffic page records local League Client API activity for debugging and endpoint discovery.

## Captured traffic

- REST requests and responses made by League Account Manager through its shared LCU connector.
- Full incoming and outgoing frames for the tracker's League LCU WebSocket connection, including direction, opcode envelope, URI, event type, and event payload.
- Method, endpoint/query, redacted request headers, status, duration, request body, and response body where available.
- Sensitive token, password, authorization, credential, and secret values are redacted before storage or export.

The page does not intercept hidden HTTP requests made internally by the League Client UI. Capturing all native client HTTP traffic requires launching the client through a local proxy or debugger.

## Controls

- Search by endpoint, method, request body, or response body.
- Filter between REST and WebSocket traffic.
- Select individual rows with the Export checkbox.
- Select all currently visible rows.
- Export selected rows to formatted JSON.
- Clear all captured traffic.

The tracker keeps the newest 1,000 records in memory.