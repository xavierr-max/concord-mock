import { useEffect, useRef, useState } from 'react'
import { getMicrophoneStream, listAudioInputs, monitorAudioLevel, stopMediaStream } from '../services/mediaDevices.js'

export function MicrophoneTest() {
  const [devices, setDevices] = useState([])
  const [deviceId, setDeviceId] = useState('')
  const [status, setStatus] = useState('Aguardando início')
  const [error, setError] = useState('')
  const [level, setLevel] = useState(0)
  const [volume, setVolume] = useState(.45)
  const [muted, setMuted] = useState(false)
  const [running, setRunning] = useState(false)
  const audioRef = useRef(null)
  const streamRef = useRef(null)
  const stopMonitorRef = useRef(() => {})
  const operationRef = useRef(0)

  const refreshDevices = async () => setDevices(await listAudioInputs())
  const release = () => {
    stopMonitorRef.current()
    stopMonitorRef.current = () => {}
    stopMediaStream(streamRef.current)
    streamRef.current = null
    if (audioRef.current) { audioRef.current.pause(); audioRef.current.srcObject = null }
  }
  const stop = () => {
    operationRef.current += 1
    release()
    setLevel(0); setRunning(false); setStatus('Teste encerrado')
  }
  const start = async () => {
    stop()
    const operation = operationRef.current
    setError(''); setStatus('Solicitando permissão…')
    try {
      const stream = await getMicrophoneStream(deviceId || undefined)
      if (operation !== operationRef.current) { stopMediaStream(stream); return }
      streamRef.current = stream
      if (audioRef.current) {
        audioRef.current.srcObject = stream
        audioRef.current.volume = volume
        audioRef.current.muted = muted
        await audioRef.current.play()
      }
      stopMonitorRef.current = monitorAudioLevel(stream, setLevel)
      await refreshDevices()
      const label = stream.getAudioTracks()[0]?.label
      setStatus(label ? `Capturando: ${label}` : 'Microfone ativo')
      setRunning(true)
    } catch (reason) { stop(); setStatus('Falha no acesso'); setError(reason.message) }
  }

  useEffect(() => {
    void listAudioInputs().then(setDevices).catch(() => {})
    return () => { operationRef.current += 1; release() }
  }, [])
  useEffect(() => { if (audioRef.current) audioRef.current.volume = volume }, [volume])
  useEffect(() => { if (audioRef.current) audioRef.current.muted = muted }, [muted])

  return <div className="microphone-test"><audio ref={audioRef} /><div className="mic-warning">Use fones de ouvido para evitar microfonia.</div><label>Dispositivo de entrada<select value={deviceId} disabled={running} onChange={event => setDeviceId(event.target.value)}><option value="">Padrão do sistema</option>{devices.map(device => <option key={device.deviceId} value={device.deviceId}>{device.label || `Microfone ${devices.indexOf(device) + 1}`}</option>)}</select></label><div className="mic-status"><span>{status}</span><div className="level-meter" role="meter" aria-label="Nível do microfone" aria-valuemin="0" aria-valuemax="100" aria-valuenow={Math.round(level * 100)}><i style={{ width: `${level * 100}%` }} /></div></div><label>Volume do retorno: {Math.round(volume * 100)}%<input type="range" min="0" max="1" step=".05" value={volume} onChange={event => setVolume(Number(event.target.value))} /></label><label className="mic-mute"><input type="checkbox" checked={muted} onChange={event => setMuted(event.target.checked)} /> Silenciar retorno</label>{error && <div className="form-error">{error}</div>}<div className="mic-actions"><button className="primary-button" disabled={running} onClick={start}>Iniciar teste</button><button className="secondary-button" disabled={!running} onClick={stop}>Parar teste</button></div></div>
}
