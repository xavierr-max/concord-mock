export function Loading({ label = 'Carregando…' }) { return <div className="state-view"><span className="spinner" />{label}</div> }
export function Empty({ title, detail }) { return <div className="state-view empty"><strong>{title}</strong>{detail && <span>{detail}</span>}</div> }
export function Toast({ message, onClose }) { return message && <div className="toast" role="status">{message}<button onClick={onClose}>×</button></div> }
