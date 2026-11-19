import { LogOut, Settings } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'

import { LanguageSwitcher } from '../../components/LanguageSwitcher'
import { useAuth } from '../auth/useAuth'
import { Avatar } from './Avatar'

/**
 * Compact profile popover: identity, language, and the actions that used to sit in the header.
 * Future profile settings get a slot here instead of adding more icons to the sidebar.
 */
export function ProfileMenu() {
  const { t } = useTranslation()
  const { user, logout } = useAuth()
  const [open, setOpen] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return

    const onPointerDown = (event: MouseEvent) => {
      if (!containerRef.current?.contains(event.target as Node)) {
        setOpen(false)
      }
    }

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false)
    }

    document.addEventListener('mousedown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)

    return () => {
      document.removeEventListener('mousedown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open])

  const handle = user?.userHandle ?? user?.login ?? '?'

  return (
    <div ref={containerRef} className="relative w-full">
      <button
        type="button"
        onClick={() => setOpen((current) => !current)}
        aria-expanded={open}
        aria-label={t('profile.settings')}
        className="flex w-full items-center gap-2 rounded-lg px-1 py-1 text-left transition hover:bg-ink-100/70"
      >
        <Avatar name={handle} size={28} />
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-medium leading-tight">@{user?.userHandle}</p>
          <p className="truncate text-[11px] leading-tight text-ink-400">{user?.login}</p>
        </div>
        <LanguageSwitcher className="mr-1" />
      </button>

      {open ? (
        <div
          role="menu"
          className="absolute left-0 right-0 top-[calc(100%+6px)] z-40 animate-fade-in-up overflow-hidden rounded-lg border border-ink-200 bg-white shadow-lg"
        >
          <div className="border-b border-ink-100 px-3 py-2">
            <p className="truncate text-[13px] font-medium">@{user?.userHandle}</p>
            <p className="truncate text-[11px] text-ink-400">{user?.login}</p>
          </div>

          <div className="p-1">
            <button
              type="button"
              role="menuitem"
              disabled
              className="flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-[12px] text-ink-400"
            >
              <Settings size={13} />
              {t('profile.settings')}
              <span className="ml-auto text-[10px] uppercase tracking-wide">{t('profile.soon')}</span>
            </button>

            <button
              type="button"
              role="menuitem"
              onClick={() => void logout()}
              className="flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-[12px] text-ink-700 transition hover:bg-ink-100 hover:text-ink-900"
            >
              <LogOut size={13} />
              {t('profile.logout')}
            </button>
          </div>
        </div>
      ) : null}
    </div>
  )
}
