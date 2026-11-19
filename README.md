# YuChat

A minimalist, security-first real-time chat platform. Single-page React UI, ASP.NET Core API,
SignalR for realtime, PostgreSQL for storage, and every message encrypted with AES-256-GCM
before it touches the database.

The interface ships in **English and Japanese** and can be switched at any time from the
language toggle in the sidebar or on the login screen. The choice is remembered per browser, and
Japanese is selected automatically when the browser prefers it.

```
.
├── YuChat.slnx     solution: backend + tests
├── backend/        ASP.NET Core Web API (.NET 10) + SignalR + EF Core
├── backend.Tests/  xUnit: encryption, access control, system feed and search tests
├── frontend/       Vite + React + TypeScript + Tailwind CSS
├── .env.example    template for backend/.env (secrets, connection string)
└── README.md
```


---

## 1. Requirements

| Tool | Version |
|---|---|
| .NET SDK | 10.0+ |
| Node.js | 20+ |
| PostgreSQL | 14+ (local, or a Render database) |

---

## 2. Configuration

Copy the template and fill in the values:

```bash
cp .env.example backend/.env
```

Generate the two secrets:

```bash
openssl rand -hex 32      # -> Jwt__Key
openssl rand -base64 32   # -> Encryption__MasterKey
```

### Database

`ConnectionStrings__DefaultConnection` accepts either form and normalizes it automatically:

- **Render "External Database URL"** — `postgres://user:pass@host:5432/dbname`
- **Npgsql string** — `Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Require`

For a Render database connecting from outside Render, keep SSL enabled (the URI parser does this
for you). The free Render PostgreSQL instance is deleted after ~90 days.

### Environment variables

`backend/.env` is loaded automatically on `dotnet run`. In production set the same names as real
environment variables (double underscore = nested configuration key):

| Variable | Purpose |
|---|---|
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection |
| `Jwt__Key` | HS256 signing key, ≥32 bytes |
| `Encryption__MasterKey` | Base64 32-byte AES key |
| `Frontend__Origin` | Allowed CORS origin (`http://localhost:5173`) |
| `Frontend__UseRefreshCookie` | `true` keeps the refresh token in an httpOnly cookie |

> **Back up `Encryption__MasterKey`.** If it is lost, existing messages cannot be decrypted.
> To rotate it later, move the old value into `Encryption__KeyRing__1`, set a new master key and
> bump `Encryption__ActiveKeyVersion` to `2`; old messages stay readable.

---

## 3. Run it

Two terminal windows, from the repository root:

```bash
# Terminal 1 — backend on http://localhost:5080
cd backend
dotnet tool restore
dotnet ef database update          # or: dotnet run -- --seed  (migrates + seeds demo data)
dotnet run --urls http://localhost:5080

# Terminal 2 — frontend on http://localhost:5173
cd frontend
npm install
npm run dev
```

Then open <http://localhost:5173>. The backend needs `backend/.env` to exist first (step 2);
`dotnet run` reads it automatically in Development.

The Vite dev server proxies `/api` and `/hubs` to `http://localhost:5080`, so the browser talks to a
single origin (no CORS juggling, and the httpOnly refresh cookie just works).

### Demo data

```bash
cd backend
dotnet run -- --seed
```

| Login | Password | User ID |
|---|---|---|
| `alice` | `password123` | `@alice_dev` |
| `bob` | `password123` | `@bob_design` |
| `carol` | `password123` | `@carol_ops` |

### Useful commands

```bash
dotnet build                                                 # whole solution (backend + tests), 0 warnings
cd backend.Tests && dotnet run                               # 48 tests (crypto, access control, feed, search)
cd frontend && npm run build                                 # type-check + production build
cd frontend && npm run gen:api                               # regenerate src/api/schema.d.ts (backend must run)
cd backend && dotnet ef migrations add <Name> --output-dir Data/Migrations   # needs `dotnet tool restore` once
```

> `dotnet test` is not used: on the .NET 10 SDK the xUnit v3 runner is a self-hosting executable,
> so the project is run directly with `dotnet run`.

### Deploy to Heroku

The repository ships a single-container image (`Dockerfile`): the Vite build is published into
`wwwroot` and served by the API, so `/api`, `/hubs/chat` and the refresh cookie share one origin.

```bash
heroku create <app> --region us
heroku stack:set container -a <app>
heroku addons:create heroku-postgresql:essential-0 -a <app>

heroku config:set \
  ASPNETCORE_ENVIRONMENT=Production \
  ConnectionStrings__DefaultConnection="$(heroku config:get DATABASE_URL -a <app>)" \
  Jwt__Key="$(openssl rand -hex 32)" \
  Encryption__MasterKey="$(openssl rand -base64 32)" \
  Frontend__Origin="https://<app>.herokuapp.com" \
  Frontend__UseRefreshCookie=true \
  Database__MaxPoolSize=8 \
  -a <app>

heroku git:remote -a <app>
git push heroku main          # Heroku builds the image; the release phase runs `--migrate`
heroku ps:type web=basic -a <app>
heroku ps:scale web=1 -a <app>
```

Notes:

- `Database__MaxPoolSize=8` keeps one dyno (+ release phase + `pg:psql`) inside the 20-connection
  limit of `essential-0`.
- Exactly one web dyno: presence and typing indicators live in process memory.
- `--migrate` applies EF Core migrations; `--seed` must not be used in production (demo passwords).
- Never lose `Encryption__MasterKey` — without it the stored messages stay unreadable.

---

## 4. Feature overview

**Accounts** — register / log in / log out, then claim a unique `@user_id`
(4–24 chars, `a-zA-Z0-9_`, case-insensitive uniqueness).

**Search** — a sidebar search bar finds conversations (group names, and the peer of your own
direct chats) and people by handle in one request. Groups are discoverable by name even when you
are not a member, but the results only list them: opening one without membership returns 404.

**Contacts** — one place for the friend list and incoming/outgoing requests.
Clicking a contact opens (or reuses) a 1-on-1 chat, so the row itself is the primary action.
Removing a contact is a two-step inline confirmation; the existing chat and its history survive.

**Chats** — 1-on-1 chats (contacts only, one room per pair) and groups. "Start a chat" opens a single
dialog with a Direct tab (pick a contact) and a Group tab (name, contacts, people added by user ID).
Non-contacts receive an invitation instead of being added, optionally with a friend request.

**Notifications feed** — every user has one pinned `System` room that behaves like a chat: friend
requests, group invitations, acceptances and contact removals land there with inline actions and an
unread badge. Only the event type plus references are stored (no rendered sentence), so the whole
history re-renders in the reader's language.

**Realtime (SignalR)** — messages, read receipts, typing indicators, presence, new chats and
invitations. On connect the server already subscribes the connection to every chat the user belongs
to, so messages keep arriving in rooms that are not currently open.

**History** — keyset pagination by message timestamp (`beforeSentAt`, 30 per page by default,
50 max), oldest-first rendering.

**Localization** — English and Japanese, switchable at runtime without a reload.

---

## 5. Security model

| Area | Implementation |
|---|---|
| Passwords | BCrypt, work factor 12; never logged |
| Sessions | 15-minute access JWT in memory + rotating refresh token (SHA-256 hash at rest) in an httpOnly cookie |
| Refresh reuse | Replaying a rotated token revokes the whole token family |
| SignalR auth | `access_token` is accepted from the query string **only** for `/hubs/chat` |
| Message encryption | AES-256-GCM, random 12-byte nonce, 16-byte tag per message, key version stored per row |
| Ciphertext binding | AAD = `chatRoomId ‖ messageId`, so rows cannot be swapped in the database without decryption failing |
| Authorization | One `ChatAccessService` used by both controllers and the hub; the user id always comes from the validated principal |
| Enumeration | Unknown chats/invitations return `404`, not `403` |
| Rate limiting | 10 auth requests/min per IP, 20 messages/10s and 10 typing events/5s per user |
| Errors | RFC 7807 `ProblemDetails` with a machine-readable `code` extension |
| Validation | Server-side length/format limits on login, handle, group name, message body, group size |

The header badge reads **"Encrypted at rest"** rather than "end-to-end": the server holds the key in
order to deliver plaintext to members, so this is not end-to-end encryption.

---

## 6. Deliberate scope limits

Not implemented, by design or for v1:

- Email addresses, password reset, account recovery (the platform has logins only)
- Message editing, deletion, or search (search is impossible over at-rest ciphertext without a blind index)
- Leaving a group, removing members, deleting a chat
- Group roles (everyone is a peer), message threads, attachments, reactions
- Multi-instance scale-out: presence and typing use an in-memory tracker, so one server process only
  (a Redis backplane would be the next step)
- Group history for newly invited members starts at their join time
