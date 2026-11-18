function hashColor(name: string) {
  let hash = 0
  for (let index = 0; index < name.length; index += 1) {
    hash = (hash * 31 + name.charCodeAt(index)) % 360
  }
  return hash
}

export function Avatar({
  name,
  size = 32,
  group = false,
  online,
}: {
  name: string
  size?: number
  group?: boolean
  online?: boolean
}) {
  const hue = hashColor(name)
  const initials = name
    .replace(/^@/, '')
    .slice(0, 2)
    .toUpperCase()

  return (
    <div className="relative shrink-0" style={{ width: size, height: size }}>
      <div
        className="flex h-full w-full items-center justify-center rounded-full font-semibold text-white select-none"
        style={{
          fontSize: Math.max(10, size * 0.36),
          background: group
            ? 'linear-gradient(135deg, #3f3f46, #18181b)'
            : `linear-gradient(135deg, hsl(${hue} 28% 46%), hsl(${hue} 32% 30%))`,
        }}
        aria-hidden="true"
      >
        {group ? '#' : initials}
      </div>

      {online !== undefined ? (
        <span
          className={`absolute -bottom-0.5 -right-0.5 h-2.5 w-2.5 rounded-full border-2 border-white ${
            online ? 'bg-emerald-500' : 'bg-ink-300'
          }`}
        />
      ) : null}
    </div>
  )
}
