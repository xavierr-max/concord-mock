export function Avatar({ user, size = 'md', speaking = false }) {
  const name = user?.displayName || user?.username || user?.userId || '?'
  return <span className={`user-avatar avatar-${size} ${speaking ? 'is-speaking' : ''}`}>
    {user?.avatar ? <img src={user.avatar} alt="" /> : initials(name)}
  </span>
}

function initials(value) { return value.split(/\s+/).slice(0, 2).map(part => part[0]).join('').toUpperCase() }
