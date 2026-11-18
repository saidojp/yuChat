# YuChat — frontend

Vite + React 19 + TypeScript + Tailwind CSS 4 single-page app.

See the [root README](../README.md) for the full setup. Short version:

```bash
npm install
npm run dev      # http://localhost:5173, proxies /api and /hubs to :5080
npm run build    # type-check + production build
npm run gen:api  # regenerate src/api/schema.d.ts from the running backend
```

## Layout

```
src/
├── api/          client.ts (fetch + silent refresh), types.ts, schema.d.ts (generated)
├── components/   Modal, LanguageSwitcher
├── features/
│   ├── auth/     AuthPage, AuthProvider (in-memory access token), useAuth
│   ├── onboarding/  claim @user_id
│   └── chat/     WorkspacePage, Sidebar, ChatView, MessageList and modals
├── locales/      en.ts, ja.ts — the two supported languages
├── realtime/     typed SignalR wrapper
├── i18n.ts       i18next setup, language persistence
└── App.tsx       routes + providers
```

## Notes

- The access token is kept **in memory only**; a page reload restores the session through the
  httpOnly refresh cookie. That is why every route renders a short splash while bootstrapping.
- `src/api/schema.d.ts` is generated, generated code is committed, and `openapi.json` is not.
- Language is stored in `localStorage` under `yuchat.language`.
