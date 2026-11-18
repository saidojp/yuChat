import { useTranslation } from 'react-i18next'

import type { ChatSummary } from '../../api/types'
import { Modal } from '../../components/Modal'

export function MembersModal({ chat, onClose }: { chat: ChatSummary; onClose: () => void }) {
  const { t } = useTranslation()

  return (
    <Modal title={chat.title} onClose={onClose}>
      <p className="mb-2 text-[11px] text-ink-400">{t('chat.members', { count: chat.participants.length })}</p>

      <ul className="space-y-0.5">
        {chat.participants.map((participant) => (
          <li key={participant.userId} className="flex items-center gap-2 rounded-lg px-2 py-1.5 hover:bg-ink-50">
            <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-ink-900 text-[10px] font-semibold text-white">
              {participant.userHandle.slice(0, 2).toUpperCase()}
            </div>
            <span className="truncate text-[13px]">@{participant.userHandle}</span>
          </li>
        ))}
      </ul>
    </Modal>
  )
}
