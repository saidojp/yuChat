import { X } from 'lucide-react'
import { useEffect, type ReactNode } from 'react'

type ModalProps = {
  title: string
  onClose: () => void
  children: ReactNode
  footer?: ReactNode
  width?: string
}

export function Modal({ title, onClose, children, footer, width = 'max-w-md' }: ModalProps) {
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }

    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center bg-ink-950/20 p-4 pt-[10vh] backdrop-blur-[2px]">
      <div
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className={`w-full ${width} animate-fade-in-up rounded-xl border border-ink-200 bg-white shadow-xl`}
      >
        <div className="flex items-center justify-between border-b border-ink-100 px-4 py-3">
          <h2 className="text-sm font-semibold">{title}</h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="rounded-md p-1 text-ink-400 transition hover:bg-ink-100 hover:text-ink-700"
          >
            <X size={15} />
          </button>
        </div>

        <div className="max-h-[60vh] overflow-y-auto px-4 py-3 scrollbar-thin">{children}</div>

        {footer ? <div className="border-t border-ink-100 px-4 py-3">{footer}</div> : null}
      </div>
    </div>
  )
}
