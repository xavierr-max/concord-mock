import logo from '../assets/concord-logo.png'

export function Brand({ compact = false }) {
  return <div className={`brand ${compact ? 'brand-compact' : ''}`}>
    <img src={logo} alt="Concord" />
    {!compact && <span>concord</span>}
  </div>
}
