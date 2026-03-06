const ACTIVE_WINDOW_DAYS = 30;
const ACTIVE_WINDOW_MS = ACTIVE_WINDOW_DAYS * 24 * 60 * 60 * 1000;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (request.method === "POST" && url.pathname === "/v1/ping") {
      return handlePing(request, env);
    }

    if (request.method === "GET" && url.pathname === "/v1/stats") {
      return handleStats(env);
    }

    return json({ ok: false, error: "Not found" }, 404);
  },
};

async function handlePing(request, env) {
  const body = await request.json().catch(() => null);
  const installId = body?.installId;
  const plugin = body?.plugin ?? "unknown";
  const version = body?.version ?? "unknown";

  if (!isValidInstallId(installId)) {
    return json({ ok: false, error: "Invalid installId" }, 400);
  }

  const now = Date.now();
  await env.ACTIVE_USERS.put(`install:${installId}`, "", {
    metadata: {
      plugin,
      version,
      lastSeen: now,
    },
  });

  const activeUsers = await countActiveUsers(env);
  return json({
    ok: true,
    activeUsers,
    activeWindowDays: ACTIVE_WINDOW_DAYS,
  });
}

async function handleStats(env) {
  const activeUsers = await countActiveUsers(env);
  return json({
    ok: true,
    activeUsers,
    activeWindowDays: ACTIVE_WINDOW_DAYS,
  });
}

async function countActiveUsers(env) {
  const cutoff = Date.now() - ACTIVE_WINDOW_MS;
  let cursor = undefined;
  let total = 0;

  do {
    const page = await env.ACTIVE_USERS.list({
      prefix: "install:",
      cursor,
      limit: 1000,
    });

    for (const key of page.keys) {
      const lastSeen = key.metadata?.lastSeen ?? 0;
      if (lastSeen >= cutoff) {
        total += 1;
      }
    }

    cursor = page.cursor;
    if (page.list_complete) {
      break;
    }
  } while (cursor);

  return total;
}

function isValidInstallId(value) {
  return typeof value === "string" && /^[a-zA-Z0-9-]{16,64}$/.test(value);
}

function json(payload, status = 200) {
  return new Response(JSON.stringify(payload), {
    status,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "access-control-allow-origin": "*",
      "cache-control": "no-store",
    },
  });
}
