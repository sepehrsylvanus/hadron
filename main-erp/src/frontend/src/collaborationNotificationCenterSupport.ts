export type LatestRequestGate = {
  begin: () => number
  invalidate: () => void
  isLatest: (requestId: number) => boolean
}

export function createLatestRequestGate(): LatestRequestGate {
  let latestRequestId = 0

  return {
    begin: () => ++latestRequestId,
    invalidate: () => { latestRequestId += 1 },
    isLatest: requestId => requestId === latestRequestId,
  }
}

type FocusTarget = Pick<HTMLElement, 'focus'>
type FrameScheduler = (callback: () => void) => unknown

export function focusOnNextFrame(
  target: FocusTarget | null,
  schedule: FrameScheduler = callback => window.requestAnimationFrame(callback),
) {
  if (!target) return
  schedule(() => target.focus())
}
