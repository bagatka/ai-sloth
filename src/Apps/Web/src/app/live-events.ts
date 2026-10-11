import { apiElsewhere } from "./host"

// A stream of the host's events that follows on through the host's restarts. The browser reconnects
// by itself after a dropped connection; when the host answers with something other than a stream
// instead, as while it restarts, this opens it again, waiting a little longer each time.

const firstWait = 1000
const longestWait = 30_000

export class LiveEvents {
  private readonly url: () => URL
  private readonly listen: (source: EventSource) => void
  private readonly changed: (live: boolean) => void
  private source: EventSource | null = null
  private wait = firstWait
  private reopenTimer = 0

  /**
   * @param url Where to read from, asked again each time the stream opens, so it can resume.
   * @param listen Adds the listeners of the stream's events.
   * @param changed Called with true once events arrive as they happen, and false when they stop.
   */
  constructor(
    url: () => URL,
    listen: (source: EventSource) => void,
    changed: (live: boolean) => void
  ) {
    this.url = url
    this.listen = listen
    this.changed = changed
  }

  open() {
    if (this.source || this.reopenTimer) {
      return
    }

    const source = new EventSource(this.url(), {
      withCredentials: apiElsewhere,
    })
    this.listen(source)
    source.onopen = () => {
      this.wait = firstWait
      this.changed(true)
    }
    source.onerror = () => {
      this.changed(false)
      if (source.readyState !== EventSource.CLOSED) {
        return
      }

      this.source = null
      this.reopenTimer = window.setTimeout(() => {
        this.reopenTimer = 0
        this.open()
      }, this.wait)
      this.wait = Math.min(this.wait * 2, longestWait)
    }
    this.source = source
  }

  close() {
    window.clearTimeout(this.reopenTimer)
    this.reopenTimer = 0
    this.source?.close()
    this.source = null
  }
}
