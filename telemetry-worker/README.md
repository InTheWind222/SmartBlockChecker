# SmartBlock active-user worker

This Cloudflare Worker stores one anonymous install key per plugin install and reports active installs seen in the last 30 days.

## Endpoints

- `POST /v1/ping`
- `GET /v1/stats`

## Deploy

1. Create a Cloudflare KV namespace.
2. Replace `REPLACE_WITH_YOUR_KV_NAMESPACE_ID` in `wrangler.toml`.
3. Deploy with `wrangler deploy`.
4. Copy the worker base URL into the plugin `Telemetry Endpoint` setting.
5. Set the GitHub Actions secret `TELEMETRY_STATS_URL` to `https://your-worker.example.workers.dev/v1/stats`.

## Payload

`POST /v1/ping`

```json
{
  "installId": "anonymous-random-id",
  "plugin": "SmartBlockChecker",
  "version": "0.0.0.20"
}
```

The worker does not need character names, account IDs, or any gameplay payloads.
