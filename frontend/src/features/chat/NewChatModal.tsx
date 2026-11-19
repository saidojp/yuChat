import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Info, Plus, X } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'

import { api, ApiError } from '../../api/client'
import type { UserSearchResult } from '../../api/types'
import { Modal } from '../../components/Modal'

type Mode = 'direct' | 'group'

type AddedUser = {
  userId: string
  userHandle: string
  isFriend: boolean
  alsoSendFriendRequest: boolean
}

/**
 * One entry point for every new conversation: a direct chat from the contacts list, or a group
 * built from picked contacts plus people invited by user ID.
 */
export function NewChatModal({
  onClose,
  onChatStarted,
}: {
  onClose: () => void
  onChatStarted: (chatId: string) => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const [mode, setMode] = useState<Mode>('direct')
  const [error, setError] = useState<string | null>(null)

  // ---- group state ----
  const [name, setName] = useState('')
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [added, setAdded] = useState<AddedUser[]>([])
  const [handleQuery, setHandleQuery] = useState('')

  const friends = useQuery({ queryKey: ['friends'], queryFn: api.friends })
  const friendList = friends.data?.friends ?? []

  const fail = (caught: unknown) =>
    setError(t(`errors.${caught instanceof ApiError ? caught.code : 'generic'}`, { defaultValue: t('errors.generic') }))

  const startDirect = useMutation({
    mutationFn: (userId: string) => api.startDirect(userId),
    onSuccess: (chat) => {
      void queryClient.invalidateQueries({ queryKey: ['chats'] })
      onChatStarted(chat.id)
      onClose()
    },
    onError: fail,
  })

  const lookup = useMutation({ mutationFn: (query: string) => api.searchUsers(query) })

  const createGroup = useMutation({
    mutationFn: () =>
      api.createGroup({
        name: name.trim(),
        friendIds: [...selected],
        additionalUsers: added
          .filter((entry) => !entry.isFriend && !selected.has(entry.userId))
          .map((entry) => ({
            userHandle: entry.userHandle,
            alsoSendFriendRequest: entry.alsoSendFriendRequest,
          })),
      }),
    onSuccess: (result) => {
      void queryClient.invalidateQueries({ queryKey: ['chats'] })
      void queryClient.invalidateQueries({ queryKey: ['friends'] })
      onChatStarted(result.chat.id)
      onClose()
    },
    onError: fail,
  })

  const resolveHandle = async () => {
    const query = handleQuery.trim()
    if (query.length < 2) return

    try {
      const results = await lookup.mutateAsync(query)
      const match = results.find((result) => result.userHandle.toLowerCase() === query.toLowerCase())

      if (!match) {
        setError(t('errors.user_not_found'))
        return
      }

      addUser(match)
    } catch (caught) {
      fail(caught)
    }
  }

  function addUser(user: UserSearchResult) {
    setError(null)
    setHandleQuery('')

    setAdded((current) =>
      current.some((entry) => entry.userId === user.id)
        ? current
        : [
            ...current,
            {
              userId: user.id,
              userHandle: user.userHandle,
              isFriend: user.friendship === 'Friends',
              alsoSendFriendRequest: false,
            },
          ],
    )
  }

  const toggleFriend = (id: string) =>
    setSelected((current) => {
      const next = new Set(current)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const canCreate = name.trim().length > 0 && (selected.size > 0 || added.length > 0)

  return (
    <Modal
      title={t('newChat.title')}
      onClose={onClose}
      footer={
        mode === 'group' ? (
          <>
            {error ? <p className="mb-2 rounded-lg bg-ink-100 px-3 py-2 text-xs text-ink-700">{error}</p> : null}
            <button
              type="button"
              onClick={() => createGroup.mutate()}
              disabled={!canCreate || createGroup.isPending}
              className="w-full rounded-lg bg-ink-900 px-3 py-2 text-sm font-medium text-white transition hover:bg-ink-800 disabled:opacity-40"
            >
              {createGroup.isPending ? t('common.loading') : t('newChat.create')}
            </button>
          </>
        ) : undefined
      }
    >
      <div className="mb-3 flex gap-1 rounded-lg bg-ink-100 p-0.5">
        {(['direct', 'group'] as const).map((entry) => (
          <button
            key={entry}
            type="button"
            onClick={() => {
              setMode(entry)
              setError(null)
            }}
            className={`flex-1 rounded-md px-2 py-1.5 text-[11px] font-medium transition ${
              mode === entry ? 'bg-white text-ink-900 shadow-sm' : 'text-ink-500 hover:text-ink-800'
            }`}
          >
            {entry === 'direct' ? t('newChat.directTab') : t('newChat.groupTab')}
          </button>
        ))}
      </div>

      {mode === 'direct' ? (
        <>
          {error ? <p className="mb-2 rounded-lg bg-ink-100 px-3 py-2 text-xs text-ink-700">{error}</p> : null}

          {friendList.length === 0 ? (
            <p className="py-6 text-center text-xs text-ink-400">{t('newChat.noFriends')}</p>
          ) : (
            <>
              <ul className="space-y-0.5">
                {friendList.map((friendship) => (
                  <li key={friendship.id}>
                    <button
                      type="button"
                      onClick={() => startDirect.mutate(friendship.user.id)}
                      disabled={startDirect.isPending}
                      className="flex w-full items-center gap-2 rounded-lg px-2 py-2 text-left transition hover:bg-ink-50 disabled:opacity-60"
                    >
                      <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-ink-900 text-[11px] font-semibold text-white">
                        {friendship.user.userHandle.slice(0, 2).toUpperCase()}
                      </div>
                      <span className="truncate text-[13px] font-medium">@{friendship.user.userHandle}</span>
                    </button>
                  </li>
                ))}
              </ul>
              <p className="mt-3 px-1 text-[10px] text-ink-400">{t('newChat.directHint')}</p>
            </>
          )}
        </>
      ) : (
        <>
          <label className="block">
            <span className="mb-1.5 block text-xs font-medium text-ink-600">{t('newChat.groupName')}</span>
            <input
              value={name}
              onChange={(event) => setName(event.target.value)}
              maxLength={80}
              placeholder={t('newChat.groupNamePlaceholder')}
              className="w-full rounded-lg border border-ink-200 px-3 py-2 text-sm outline-none transition placeholder:text-ink-400 focus:border-ink-400"
            />
          </label>

          <div className="mt-4">
            <div className="mb-1.5 flex items-center justify-between">
              <span className="text-xs font-medium text-ink-600">{t('newChat.members')}</span>
              {selected.size > 0 ? (
                <span className="text-[11px] text-ink-400">
                  {t('newChat.selectedCount', { count: selected.size })}
                </span>
              ) : null}
            </div>

            {friendList.length === 0 ? (
              <p className="text-[11px] text-ink-400">{t('newChat.noFriends')}</p>
            ) : (
              <ul className="max-h-36 space-y-0.5 overflow-y-auto scrollbar-thin">
                {friendList.map((friendship) => (
                  <li key={friendship.id}>
                    <label className="flex cursor-pointer items-center gap-2 rounded-lg px-2 py-1.5 hover:bg-ink-50">
                      <input
                        type="checkbox"
                        checked={selected.has(friendship.user.id)}
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
            <span className="mb-1.5 block text-xs font-medium text-ink-600">{t('newChat.addById')}</span>

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
                    void resolveHandle()
                  }
                }}
                placeholder={t('newChat.addByIdPlaceholder')}
                className="min-w-0 flex-1 rounded-lg border border-ink-200 px-3 py-2 text-sm outline-none transition placeholder:text-ink-400 focus:border-ink-400"
              />
              <button
                type="button"
                onClick={() => void resolveHandle()}
                disabled={lookup.isPending}
                className="inline-flex items-center gap-1 rounded-lg border border-ink-200 px-2.5 text-[11px] font-medium transition hover:border-ink-300 disabled:opacity-50"
              >
                <Plus size={11} />
                {t('newChat.add')}
              </button>
            </div>

            <p className="mt-1.5 flex items-start gap-1 text-[10px] leading-snug text-ink-400">
              <Info size={10} className="mt-0.5 shrink-0" />
              {t('newChat.invitedNote')}
            </p>

            {added.length > 0 ? (
              <ul className="mt-2 space-y-1">
                {added.map((entry) => {
                  const willInvite = !entry.isFriend && !selected.has(entry.userId)

                  return (
                    <li key={entry.userId} className="rounded-lg border border-ink-200 px-2.5 py-2">
                      <div className="flex items-center gap-2">
                        <span className="truncate text-[13px]">@{entry.userHandle}</span>

                        {willInvite ? (
                          <span className="shrink-0 rounded-full bg-ink-100 px-1.5 py-0.5 text-[10px] text-ink-500">
                            {t('newChat.willInvite')}
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

                      {willInvite ? (
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
                          {t('newChat.alsoFriendRequest')}
                        </label>
                      ) : null}
                    </li>
                  )
                })}
              </ul>
            ) : null}
          </div>
        </>
      )}
    </Modal>
  )
}
