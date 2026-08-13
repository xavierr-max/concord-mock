export async function listAudioInputs() {
  if (!navigator.mediaDevices?.enumerateDevices) return []
  const devices = await navigator.mediaDevices.enumerateDevices()
  return devices.filter(device => device.kind === 'audioinput')
}

export async function getMicrophoneStream(deviceId) {
  if (!navigator.mediaDevices?.getUserMedia)
    throw createMediaError('NO_MEDIA_DEVICES', 'Este navegador não oferece acesso ao microfone.')
  try {
    return await navigator.mediaDevices.getUserMedia({
      video: false,
      audio: deviceId ? { deviceId: { exact: deviceId } } : true,
    })
  } catch (error) {
    if (error?.name === 'NotAllowedError' || error?.name === 'SecurityError')
      throw createMediaError('MICROPHONE_DENIED', 'Permissão para usar o microfone foi negada.', error)
    if (error?.name === 'NotFoundError' || error?.name === 'DevicesNotFoundError')
      throw createMediaError('NO_MICROPHONE', 'Nenhum microfone foi encontrado.', error)
    if (error?.name === 'NotReadableError' || error?.name === 'TrackStartError')
      throw createMediaError('MICROPHONE_UNAVAILABLE', 'O microfone está ocupado ou indisponível.', error)
    throw createMediaError('MICROPHONE_ERROR', 'Não foi possível acessar o microfone.', error)
  }
}

export function stopMediaStream(stream) {
  stream?.getTracks().forEach(track => track.stop())
}

export function monitorAudioLevel(stream, onLevel) {
  const AudioContext = window.AudioContext || window.webkitAudioContext
  if (!AudioContext || !stream?.getAudioTracks().length) return () => {}
  const context = new AudioContext()
  const analyser = context.createAnalyser()
  analyser.fftSize = 512
  analyser.smoothingTimeConstant = .5
  const source = context.createMediaStreamSource(stream)
  const samples = new Float32Array(analyser.fftSize)
  let frame
  const analyse = () => {
    analyser.getFloatTimeDomainData(samples)
    let sum = 0
    for (const sample of samples) sum += sample * sample
    onLevel(Math.min(1, Math.sqrt(sum / samples.length) * 7))
    frame = requestAnimationFrame(analyse)
  }
  void context.resume()
  analyse()
  return () => {
    cancelAnimationFrame(frame)
    source.disconnect()
    analyser.disconnect()
    void context.close()
  }
}

function createMediaError(code, message, cause) {
  return Object.assign(new Error(message, { cause }), { code })
}
