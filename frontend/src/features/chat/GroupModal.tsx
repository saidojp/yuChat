import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Info, Plus, X } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'

import { api, ApiError } from '../../api/client'
import type { UserSearchResult } from '../../api/types'
import { Modal } from '../../components/Modal'

type AddedUser = {
  userId: string
  userHandle: string
  isFriend: boolean
  alsoSendFriendRequest: boolean
}

export function GroupModal({
  onClose,
  onCreated,
}: {
  onClose: () => void
  onCreated: (chatId: string) => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const [name, setName] = useState('')
  const [selectedFriends, setSelectedFriends] = useState<Set<string>>(new Set())
  const [added, setAdded] = useState<AddedUser[]>([])
  const [handleQuery, setHandleQuery] = useState('')
  const [error, setError] = useState<string | null>(null)

  const friends = useQuery({ queryKey: ['friends'], queryFn: api.friends })
  const friendList = friends.data?.friends ?? []

  const lookup = useMutation({
    mutationFn: (query: string) => api.searchUsers(query),
    onSuccess: (results) => {
      const query = handleQuery.trim().toLowerCase()
      const match = results.find((result) => result.userHandle.toLowerCase() === query)

      if (!match) {
        setError(t('errors.user_not_found'))
        return
      }

      addUser(match)
    },
    onError: () => setError(t('errors.generic')),
  })

  const addUser = (user: UserSearchResult) => {
    setError(null)

    if (added.some((entry) => entry.userId === user.id)) {
      return
    }

    setAdded((current) => [
      ...current,
      {
        userId: user.id,
        userHandle: user.userHandle,
        isFriend: user.friendship === 'Friends',
        alsoSendFriendRequest: false,
      },
    ])

    setHandleQuery('')
  }

  const createGroup = useMutation({
    mutationFn: () =>
      api.createGroup({
        name: name.trim(),
        friendIds: [...selectedFriends],
        additionalUsers: added
          .filter((entry) => !entry.isFriend && !selectedFriends.has(entry.userId))
          .map((entry) => ({ userHandle: entry.userHandle, alsoSendFriendRequest: entry.alsoSendFriendRequest })),
      }),
    onSuccess: (result) => {
      void queryClient.invalidateQueries({ queryKey: ['chats'] })
      void queryClient.invalidateQueries({ queryKey: ['friends'] })
      onCreated(result.chat.id)
      onClose()
    },
    onError: (caught) => {
      const code = caught instanceof ApiError ? caught.code : 'generic'
      setError(t(`errors.${code}`, { defaultValue: t('errors.generic') }))
    },
  })

  const toggleFriend = (id: string) => {
    setSelectedFriends((current) => {
      const next = new Set(current)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  const canCreate = name.trim().length > 0 && (selectedFriends.size > 0 || added.length > 0)

  return (
    <Modal
      title={t('group.title')}
      onClose={onClose}
      footer={
        <button
          type="button"
          onClick={() => createGroup.mutate()}
          disabled={!canCreate || createGroup.isPending}
          className="w-full rounded-lg bg-ink-900 px-3 py-2 text-sm font-medium text-white transition hover:bg-ink-800 disabled:opacity-40"
        >
          {createGroup.isPending ? t('common.loading') : t('group.create')}
        </button>
      }
    >
      <label className="block">
        <span className="mb-1.5 block text-xs font-medium text-ink-600">{t('group.name')}</span>
        <input
          value={name}
          onChange={(event) => setName(event.target.value)}
          maxLength={80}
          placeholder={t('group.namePlaceholder')}
          className="w-full rounded-lg border border-ink-200 px-3 py-2 text-sm outline-none transition placeholder:text-ink-400 focus:border-ink-400"
        />
      </label>

      <div className="mt-4">
        <div className="mb-1.5 flex items-center justify-between">
          <span className="text-xs font-medium text-ink-600">{t('group.members')}</span>
          {selectedFriends.size > 0 ? (
            <span className="text-[11px] text-ink-400">{t('group.selectedCount', { count: selectedFriends.size })}</span>
          ) : null}
        </div>

        {friendList.length === 0 ? (
          <p className="text-[11px] text-ink-400">{t('group.noFriends')}</p>
        ) : (
          <ul className="max-h-40 space-y-0.5 overflow-y-auto scrollbar-thin">
            {friendList.map((friendship) => (
              <li key={friendship.id}>
                <label className="flex cursor-pointer items-center gap-2 rounded-lg px-2 py-1.5 hover:bg-ink-50">
                  <input
                    type="checkbox"
                    checked={selectedFriends.has(friendship.user.id)}
                    onChange={() => toggleFriend(friendship.user.id)}
                    className="h-3.5 w-3.5 accent-ink-900"
                  />
                  <span className="text-[13px]">@{friendship.user.userHandle}</span>
                </label>
              </li>
            ))}
          </ul>
        )}
      </div>

      <div className="mt-4">
        <span className="mb-1.5 block text-xs font-medium text-ink-600">{t('group.addById')}</span>

        <div className="flex gap-1.5">
          <input
            value={handleQuery}
            onChange={(event) => {
              setHandleQuery(event.target.value.replace(/^@/, ''))
              setError(null)
            }}
            onKeyDown={(event) => {
              if (event.key === 'Enter') {
                event.preventDefault()
                if (handleQuery.trim().length >= 2) lookup.mutate(handleQuery.trim())
              }
            }}
            placeholder={t('group.addByIdPlaceholder')}
            className="min-w-0 flex-1 rounded-lg border border-ink-200 px-3 py-2 text-sm outline-none transition placeholder:text-ink-400 focus:border-ink-400"
          />
          <button
            type="button"
            onClick={() => handleQuery.trim().length >= 2 && lookup.mutate(handleQuery.trim())}
            disabled={lookup.isPending}
            className="inline-flex items-center gap-1 rounded-lg border border-ink-200 px-2.5 text-[11px] font-medium transition hover:border-ink-300 disabled:opacity-50"
          >
            <Plus size={11} />
            {t('group.add')}
          </button>
        </div>

        <p className="mt-1.5 flex items-start gap-1 text-[10px] leading-snug text-ink-400">
          <Info size={10} className="mt-0.5 shrink-0" />
          {t('group.invitedNote')}
        </p>

        {added.length > 0 ? (
          <ul className="mt-2 space-y-1">
            {added.map((entry) => {
              const isSelectedFriend = selectedFriends.has(entry.userId)

              return (
                <li key={entry.userId} className="rounded-lg border border-ink-200 px-2.5 py-2">
                  <div className="flex items-center gap-2">
                    <span className="truncate text-[13px]">@{entry.userHandle}</span>

                    {!entry.isFriend && !isSelectedFriend ? (
                      <span className="shrink-0 rounded-full bg-ink-100 px-1.5 py-0.5 text-[10px] text-ink-500">
                        {t('group.willInvite')}
                      </span>
                    ) : null}

                    <button
                      type="button"
                      onClick={() => setAdded((current) => current.filter((item) => item.userId !== entry.userId))}
                      aria-label={t('common.close')}
                      className="ml-auto rounded p-0.5 text-ink-400 transition hover:text-ink-800"
                    >
                      <X size={11} />
                    </button>
                  </div>

                  {!entry.isFriend && !isSelectedFriend ? (
                    <label className="mt-1.5 flex cursor-pointer items-center gap-1.5 text-[11px] text-ink-500">
                      <input
                        type="checkbox"
                        checked={entry.alsoSendFriendRequest}
                        onChange={(event) =>
                          setAdded((current) =>
                            current.map((item) =>
                              item.userId === entry.userId
                                ? { ...item, alsoSendFriendRequest: event.target.checked }
                                : item,
                            ),
                          )
                        }
                        className="h-3 w-3 accent-ink-900"
                      />
                      {t('group.alsoFriendRequest')}
                    </label>
                  ) : null}
                </li>
              )
            })}
          </ul>
        ) : null}
      </div>

      {error ? <p className="mt-3 rounded-lg bg-ink-100 px-3 py-2 text-xs text-ink-700">{error}</p> : null}
    </Modal>
  )
}
