export function Modal({ title, children, onClose, actions }) {
  return <div className="modal-backdrop" role="presentation" onMouseDown={event => event.target === event.currentTarget && onClose()}>
    <section className="modal" role="dialog" aria-modal="true" aria-label={title}>
      <header><h2>{title}</h2><button className="icon-button" onClick={onClose} aria-label="Fechar">×</button></header>
      <div className="modal-content">{children}</div>
      {actions && <footer>{actions}</footer>}
    </section>
  </div>
}
